using System.Globalization;
using System.Security.Cryptography;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;

namespace ShortsPublisher;

/// <summary>
/// Facebook story automation: copies a video to Android, selects it in the gallery,
/// and submits it using the story Share button. Caption is not added to stories.
/// </summary>
public sealed class FacebookPublisher(PublisherOptions options) : IDisposable
{
    private AndroidDriver? _driver;
    private readonly PublisherOptions _options = options;
    private const string FacebookPackage = "com.facebook.katana";

    public Task<FacebookPreparationResult> PreparePublicationAsync(
        string? filePath,
        string? caption,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("Facebook test requires an existing --file video.", filePath);
        if (!new[] { ".mp4", ".mov", ".m4v" }.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unsupported Facebook video extension.");
        if (new FileInfo(filePath).Length == 0)
            throw new InvalidOperationException("Facebook video is empty.");

        Console.Error.WriteLine("[facebook] media.hash: calculating SHA-256");
        using var input = File.OpenRead(filePath);
        var sha = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        var remoteDirectory = _options.AndroidMediaDirectory.TrimEnd('/');
        var remoteName = $"fb_{sha[..16]}_{Path.GetFileName(filePath)}";
        var remotePath = $"{remoteDirectory}/{remoteName}";
        var adb = new AdbClient(_options.AdbPath);
        var devices = adb.DevicesAsync(cancellationToken).GetAwaiter().GetResult();
        if (!devices.Split('\n').Any(line => line.Trim().StartsWith(_options.DeviceId + "\tdevice", StringComparison.Ordinal)))
            throw new InvalidOperationException("Configured Android device is not ready.");
        adb.ShellAsync(_options.DeviceId, cancellationToken, "mkdir", "-p", remoteDirectory).GetAwaiter().GetResult();
        Console.Error.WriteLine("[facebook] media.push: copying video to Android");
        adb.PushAsync(_options.DeviceId, filePath, remotePath, cancellationToken).GetAwaiter().GetResult();
        var hashOutput = adb.ShellAsync(_options.DeviceId, cancellationToken, "sha256sum", remotePath).GetAwaiter().GetResult();
        var remoteHash = hashOutput.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.Equals(sha, remoteHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Facebook Android SHA-256 mismatch.");
        Console.Error.WriteLine("[facebook] media.verify: SHA-256 matches");
        adb.ShellAsync(_options.DeviceId, cancellationToken, "am", "broadcast",
            "-a", "android.intent.action.MEDIA_SCANNER_SCAN_FILE", "-d", $"file://{remotePath}").GetAwaiter().GetResult();
        Console.Error.WriteLine("[facebook] media.scan: requested");
        var deviceTimeOutput = adb.ShellAsync(_options.DeviceId, cancellationToken,
            "date", "+%Y-%m-%d_%H:%M").GetAwaiter().GetResult().Trim();
        if (!DateTime.TryParseExact(deviceTimeOutput, "yyyy-MM-dd_HH:mm",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var deviceScanTime))
            throw new InvalidOperationException($"Cannot read Android local time: {deviceTimeOutput}");
        Console.Error.WriteLine($"[facebook] gallery.match: Android scan time={deviceScanTime:yyyy-MM-dd HH:mm}");
        Console.Error.WriteLine("[facebook] app.open: connecting to Facebook");
        Connect();
        _driver!.ActivateApp(FacebookPackage);
        Console.Error.WriteLine("[facebook] app.open: ready");

        Console.Error.WriteLine("[facebook] profile.open: locating bottom Profile tab");
        var deadline = DateTime.UtcNow.AddSeconds(20);
        IWebElement? profileTab = null;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            profileTab = _driver.FindElements(By.XPath(
                "//*[@class='android.app.ActionBar$Tab' and (starts-with(@content-desc,'Profile, tab ') or starts-with(@content-desc,'Профиль, вкладка '))]"))
                .FirstOrDefault(element => element.Displayed && element.Enabled);
            if (profileTab is not null) break;
            Thread.Sleep(500);
        }

        if (profileTab is null)
            throw new InvalidOperationException(
                "Facebook bottom Profile tab was not found. No publication attempted.");

        profileTab.Click();
        Console.Error.WriteLine("[facebook] profile.open: tapped Profile tab");

        var verifyDeadline = DateTime.UtcNow.AddSeconds(15);
        var profileVerified = false;
        while (DateTime.UtcNow < verifyDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selected = _driver.FindElements(By.XPath(
                "//*[@class='android.app.ActionBar$Tab' and (starts-with(@content-desc,'Profile, tab ') or starts-with(@content-desc,'Профиль, вкладка ')) and @selected='true']"));
            if (selected.Count > 0)
            {
                Console.Error.WriteLine("[facebook] profile.verify: Profile tab selected");
                profileVerified = true;
                break;
            }
            Thread.Sleep(500);
        }

        // The profile page can replace the bottom tab hierarchy.
        if (!profileVerified)
            Console.Error.WriteLine("[facebook] profile.verify: checking profile action");

        const string storyIndicator = "Фото профиля с непросмотренными историями";
        const string englishStoryIndicator = "Profile picture with unseen stories";
        var profileBeforeShare = _driver.PageSource;
        var hadActiveStory = profileBeforeShare.Contains(storyIndicator, StringComparison.OrdinalIgnoreCase)
            || profileBeforeShare.Contains(englishStoryIndicator, StringComparison.OrdinalIgnoreCase);
        if (hadActiveStory)
        {
            Console.Error.WriteLine("[facebook] story.reset: unseen stories present; attempting fast viewing");
            var reset = TryResetUnseenStories(adb, cancellationToken);
            Console.Error.WriteLine($"[facebook] story.reset: cleared={reset}");
            // The story viewer can leave us on a different screen. Never treat
            // absence of the indicator outside the profile as a clean baseline.
            if (!EnsureProfileAfterStoryReset(cancellationToken))
                throw new InvalidOperationException(
                    "Facebook did not return to the profile after story reset. No publication attempted.");
            var refreshedProfile = _driver.PageSource;
            hadActiveStory = HasUnseenStories(refreshedProfile);
        }
        Console.Error.WriteLine($"[facebook] story.baseline: active story indicator present={hadActiveStory}");
        Console.Error.WriteLine("[facebook] story.open: locating Add to story");
        var storyDeadline = DateTime.UtcNow.AddSeconds(20);
        IWebElement? addToStory = null;
        while (DateTime.UtcNow < storyDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            addToStory = _driver.FindElements(By.XPath(
                "//*[@class='android.widget.Button' and (@content-desc='Add to story' or @content-desc='Дополнить историю') and @clickable='true']"))
                .FirstOrDefault(element => element.Displayed && element.Enabled);
            if (addToStory is not null) break;
            Thread.Sleep(500);
        }

        if (addToStory is null)
            throw new InvalidOperationException(
                "Facebook Profile Add to story button was not found. No publication attempted.");

        Console.Error.WriteLine("[facebook] profile.verify: profile action found");
        addToStory.Click();
        Console.Error.WriteLine("[facebook] story.open: tapped Add to story");
        Console.Error.WriteLine("[facebook] gallery.inspect: looking for video tiles");
        var galleryDeadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < galleryDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_driver.FindElements(By.XPath(
                "//*[@class='android.widget.Spinner' and (starts-with(@content-desc,'Select album, ') or starts-with(@content-desc,'Выбор альбома, '))]")).Count > 0)
                break;
            Thread.Sleep(500);
        }
        var videos = _driver.FindElements(By.XPath(
            "//*[@class='android.widget.Button' and (starts-with(@content-desc,'Select video, ') or starts-with(@content-desc,'Выбрать видео, '))]"))
            .Where(element => element.Displayed && element.Enabled)
            .Select(element => new { Element = element, Description = element.GetAttribute("content-desc") ?? "" })
            .ToArray();
        Console.Error.WriteLine($"[facebook] gallery.inspect: {videos.Length} visible video tiles");
        foreach (var video in videos)
            Console.Error.WriteLine($"[facebook] gallery.video: {video.Description}");

        // Facebook exposes media date and duration, but not the filename.
        // Only select when exactly one video matches the recent Android scan time.
        var timestampedVideos = videos.Select(video =>
        {
            var russian = video.Description.StartsWith("Выбрать видео, ", StringComparison.Ordinal);
            var english = video.Description.StartsWith("Select video, ", StringComparison.Ordinal);
            if (!russian && !english)
                return new { video.Element, video.Description, Timestamp = (DateTime?)null };
            var prefix = russian ? "Выбрать видео, " : "Select video, ";
            var datePart = video.Description[prefix.Length..];
            var lastComma = datePart.LastIndexOf(", ", StringComparison.Ordinal);
            if (lastComma < 0)
                return new { video.Element, video.Description, Timestamp = (DateTime?)null };
            var culture = russian ? CultureInfo.GetCultureInfo("ru-RU") : CultureInfo.InvariantCulture;
            var parsed = DateTime.TryParseExact(datePart[..lastComma], "MMM dd, yyyy HH:mm",
                culture, DateTimeStyles.None, out var mediaTime);
            return new { video.Element, video.Description, Timestamp = parsed ? (DateTime?)mediaTime : null };
        }).Where(video => video.Timestamp.HasValue
            && (video.Timestamp.Value - deviceScanTime).Duration() <= TimeSpan.FromMinutes(3))
          .ToArray();

        var latestTimestamp = timestampedVideos.Length > 0
            ? timestampedVideos.Max(video => video.Timestamp!.Value)
            : (DateTime?)null;
        var candidates = timestampedVideos
            .Where(video => video.Timestamp == latestTimestamp)
            .ToArray();

        Console.Error.WriteLine($"[facebook] gallery.match: {candidates.Length} recent video candidates");
        if (candidates.Length != 1)
            throw new InvalidOperationException(
                $"Facebook latest video selection is ambiguous: {candidates.Length} candidates at {latestTimestamp:yyyy-MM-dd HH:mm}. No video selected or published.");

        Console.Error.WriteLine($"[facebook] gallery.select: tapping {candidates[0].Description}");
        candidates[0].Element.Click();
        Console.Error.WriteLine("[facebook] share.locate: waiting for Share / Поделиться");
        var shareDeadline = DateTime.UtcNow.AddSeconds(20);
        IWebElement? shareButton = null;
        while (DateTime.UtcNow < shareDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            shareButton = _driver.FindElements(By.XPath(
                "//*[@class='android.widget.Button' and (@content-desc='Share' or @content-desc='Поделиться') and @clickable='true']"))
                .FirstOrDefault(element => element.Displayed && element.Enabled);
            if (shareButton is not null) break;
            Thread.Sleep(500);
        }
        if (shareButton is null)
            throw new InvalidOperationException(
                "Facebook story Share / Поделиться button was not found. No publication attempted.");

        Console.Error.WriteLine("[facebook] share.tap: tapping Share / Поделиться");
        shareButton.Click();
        Console.Error.WriteLine("[facebook] share.tap: tapped; observing publication confirmation");
        var observation = System.Diagnostics.Stopwatch.StartNew();
        var nextSnapshot = TimeSpan.FromSeconds(2);
        string? lastXml = null;
        var indicatorSeen = false;
        while (observation.Elapsed < TimeSpan.FromSeconds(200))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (observation.Elapsed < nextSnapshot)
            {
                Thread.Sleep(250);
                continue;
            }

            try
            {
                var xml = _driver.PageSource;
                lastXml = xml;
                // Facebook occasionally displays a camera-shortcut prompt after Share.
                // Dismiss only this specific dialog; otherwise it hides the profile
                // and prevents the story indicator from being observed.
                if (xml.Contains("С легкостью дополняйте свою историю", StringComparison.Ordinal)
                    && xml.Contains("Добавьте быструю ссылку", StringComparison.Ordinal))
                {
                    var dismiss = _driver.FindElements(By.Id("android:id/button2"))
                        .FirstOrDefault(button => button.Displayed && button.Enabled
                            && button.Text == "Не сейчас");
                    if (dismiss is not null)
                    {
                        Console.Error.WriteLine("[facebook] share.dialog: dismissing camera shortcut prompt with Не сейчас");
                        dismiss.Click();
                    }
                }
                indicatorSeen = xml.Contains(storyIndicator, StringComparison.OrdinalIgnoreCase)
                    || xml.Contains(englishStoryIndicator, StringComparison.OrdinalIgnoreCase);
                if (indicatorSeen && !hadActiveStory)
                {
                    Console.Error.WriteLine($"[facebook] share.confirmed: new story indicator appeared after {observation.Elapsed.TotalSeconds:F1}s");
                    CleanupPublishedAndroidMedia(adb, remoteDirectory, remoteName, remotePath, sha);
                    return Task.FromResult(new FacebookPreparationResult(
                        "published", "Facebook profile acquired the unseen-stories indicator after Share.", true, sha));
                }
                if (indicatorSeen && hadActiveStory)
                    Console.Error.WriteLine("[facebook] share.verify: story indicator existed before Share; cannot confirm new publication");
            }
            catch (Exception ex) when (ex is WebDriverException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"[facebook] share.verify: UI inspection failed: {ex.Message}");
            }

            nextSnapshot += TimeSpan.FromSeconds(2);
            while (nextSnapshot <= observation.Elapsed)
                nextSnapshot += TimeSpan.FromSeconds(2);
        }
        Console.Error.WriteLine($"[facebook] share.unconfirmed: no new story indicator after {observation.Elapsed.TotalSeconds:F1}s");
        SaveFailureDiagnostic(lastXml);
        return Task.FromResult(new FacebookPreparationResult(
            "facebook_share_unconfirmed",
            hadActiveStory
                ? "Story indicator already existed before Share; new publication cannot be confirmed by this signal."
                : "Story indicator did not appear within 200 seconds after Share.",
            false, sha));
    }

    private void CleanupPublishedAndroidMedia(
        AdbClient adb, string remoteDirectory, string remoteName, string remotePath, string sha)
    {
        // A cleanup failure must never turn a confirmed post into a retryable failure.
        try
        {
            if (!remoteName.StartsWith($"fb_{sha[..16]}_", StringComparison.Ordinal)
                || !remotePath.StartsWith(remoteDirectory + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe Facebook Android media cleanup path.");
            Console.Error.WriteLine("[facebook] cleanup.media: removing uploaded Android copy");
            adb.ShellAsync(_options.DeviceId, CancellationToken.None, "rm", "--", remotePath)
                .GetAwaiter().GetResult();
            adb.ShellAsync(_options.DeviceId, CancellationToken.None, "am", "broadcast",
                "-a", "android.intent.action.MEDIA_SCANNER_SCAN_FILE", "-d", $"file://{remotePath}")
                .GetAwaiter().GetResult();
            Console.Error.WriteLine("[facebook] cleanup.media: completed");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[facebook] cleanup.media warning: {ex.Message}");
        }
    }

    private static void SaveFailureDiagnostic(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return;
        try
        {
            var directory = Path.Combine("diagnostics", "facebook");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory,
                $"unconfirmed_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}.xml");
            File.WriteAllText(path, xml);
            Console.Error.WriteLine($"[facebook] diagnostics.xml: saved last unconfirmed screen to {path}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[facebook] diagnostics.xml warning: {ex.Message}");
        }
    }

    private bool EnsureProfileAfterStoryReset(CancellationToken cancellationToken)
    {
        const string profileAction =
            "//*[@class='android.widget.Button' and (@content-desc='Add to story' or @content-desc='Дополнить историю') and @clickable='true']";
        const string profileTab =
            "//*[@class='android.app.ActionBar$Tab' and (starts-with(@content-desc,'Profile, tab ') or starts-with(@content-desc,'Профиль, вкладка '))]";
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_driver!.FindElements(By.XPath(profileAction)).Any(e => e.Displayed && e.Enabled))
            {
                Console.Error.WriteLine("[facebook] story.reset: profile verified");
                return true;
            }

            var tab = _driver.FindElements(By.XPath(profileTab))
                .FirstOrDefault(e => e.Displayed && e.Enabled);
            if (tab is not null)
            {
                Console.Error.WriteLine("[facebook] story.reset: returning via Profile tab");
                tab.Click();
            }
            else
            {
                Console.Error.WriteLine("[facebook] story.reset: returning via Back");
                _driver.Navigate().Back();
            }
            Thread.Sleep(700);
        }
        Console.Error.WriteLine("[facebook] story.reset: profile recovery timed out");
        return false;
    }

    private static bool HasUnseenStories(string xml) =>
        xml.Contains("Фото профиля с непросмотренными историями", StringComparison.OrdinalIgnoreCase)
        || xml.Contains("Profile picture with unseen stories", StringComparison.OrdinalIgnoreCase);

    private bool TryResetUnseenStories(AdbClient adb, CancellationToken cancellationToken)
    {
        // This is best-effort preflight cleanup, never evidence of a new publication.
        // Stop after 30 seconds or 20 skips; do not wait for 60-second videos.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        try
        {
            var avatar = _driver!.FindElements(By.XPath(
                "//*[@content-desc='Фото профиля с непросмотренными историями' or @content-desc='Profile picture with unseen stories']"))
                .FirstOrDefault(element => element.Displayed && element.Enabled);
            if (avatar is null)
            {
                Console.Error.WriteLine("[facebook] story.reset: profile photo indicator element not found");
                return false;
            }

            avatar.Click();
            var viewStory = (IWebElement?)null;
            while (DateTime.UtcNow < deadline && viewStory is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                viewStory = _driver.FindElements(By.XPath(
                    "//*[@class='android.widget.Button' and (@content-desc='Посмотреть историю' or @content-desc='View story') and @clickable='true']"))
                    .FirstOrDefault(element => element.Displayed && element.Enabled);
                if (viewStory is null) Thread.Sleep(300);
            }
            if (viewStory is null)
            {
                Console.Error.WriteLine("[facebook] story.reset: View story action not found");
                _driver.Navigate().Back();
                return false;
            }

            viewStory.Click();
            Thread.Sleep(700);
            for (var skip = 0; skip < 20 && DateTime.UtcNow < deadline; skip++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var xml = _driver.PageSource;
                // Returning to the profile and losing the unseen indicator is the
                // only success condition; never infer it from tap count.
                if (xml.Contains("Дополнить историю", StringComparison.Ordinal)
                    || xml.Contains("Add to story", StringComparison.Ordinal))
                {
                    var cleared = !HasUnseenStories(xml);
                    if (cleared) return true;
                    // Still unseen: reopen the viewer if it closed early.
                    var photo = _driver.FindElements(By.XPath(
                        "//*[@content-desc='Фото профиля с непросмотренными историями' or @content-desc='Profile picture with unseen stories']"))
                        .FirstOrDefault(element => element.Displayed && element.Enabled);
                    if (photo is null) break;
                    photo.Click();
                    var action = _driver.FindElements(By.XPath(
                        "//*[@class='android.widget.Button' and (@content-desc='Посмотреть историю' or @content-desc='View story') and @clickable='true']"))
                        .FirstOrDefault(element => element.Displayed && element.Enabled);
                    if (action is null) break;
                    action.Click();
                }
                // Right-side tap advances the current Facebook story immediately.
                adb.ShellAsync(_options.DeviceId, cancellationToken, "input", "tap", "920", "1050")
                    .GetAwaiter().GetResult();
                Console.Error.WriteLine($"[facebook] story.reset: skipped story {skip + 1}");
                Thread.Sleep(350);
            }

            // Exit the viewer and inspect the actual profile state.
            _driver.Navigate().Back();
            Thread.Sleep(500);
            return !HasUnseenStories(_driver.PageSource)
                && (_driver.PageSource.Contains("Дополнить историю", StringComparison.Ordinal)
                    || _driver.PageSource.Contains("Add to story", StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is WebDriverException or InvalidOperationException)
        {
            Console.Error.WriteLine($"[facebook] story.reset: failed: {ex.Message}");
            try { _driver?.Navigate().Back(); } catch { }
            return false;
        }
    }

    private void Connect()
    {
        if (_driver is not null) return;
        var caps = new AppiumOptions();
        caps.PlatformName = "Android";
        caps.AutomationName = "UiAutomator2";
        caps.DeviceName = "Pixel 6a";
        caps.AddAdditionalAppiumOption("udid", _options.DeviceId);
        caps.AddAdditionalAppiumOption("appPackage", FacebookPackage);
        caps.AddAdditionalAppiumOption("noReset", true);
        caps.AddAdditionalAppiumOption("newCommandTimeout", 180);
        _driver = new AndroidDriver(new Uri(_options.AppiumServer), caps, TimeSpan.FromMinutes(3));
    }

    public void Dispose()
    {
        try { _driver?.Quit(); } catch { }
        _driver?.Dispose();
    }
}

public sealed record FacebookPreparationResult(string Status, string Message, bool Published = false, string? Sha256 = null);
