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
        Console.Error.WriteLine("[facebook] share.tap: tapped; capturing post-share UI XML");
        var diagnosticDirectory = Path.Combine("diagnostics", "facebook",
            DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(diagnosticDirectory);
        var observation = System.Diagnostics.Stopwatch.StartNew();
        var nextSnapshot = TimeSpan.FromSeconds(2);
        var snapshot = 0;
        var indicatorSeen = false;
        while (observation.Elapsed < TimeSpan.FromSeconds(200))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (observation.Elapsed < nextSnapshot)
            {
                Thread.Sleep(250);
                continue;
            }

            snapshot++;
            var diagnosticFile = Path.Combine(diagnosticDirectory, $"screen_{snapshot:000}.xml");
            try
            {
                var xml = _driver.PageSource;
                File.WriteAllText(diagnosticFile, xml);
                Console.Error.WriteLine($"[facebook] diagnostics.xml: {diagnosticFile}");
                indicatorSeen = xml.Contains(storyIndicator, StringComparison.OrdinalIgnoreCase)
                    || xml.Contains(englishStoryIndicator, StringComparison.OrdinalIgnoreCase);
                if (indicatorSeen && !hadActiveStory)
                {
                    Console.Error.WriteLine($"[facebook] share.confirmed: new story indicator appeared after {observation.Elapsed.TotalSeconds:F1}s");
                    return Task.FromResult(new FacebookPreparationResult(
                        "published", "Facebook profile acquired the unseen-stories indicator after Share.", true));
                }
                if (indicatorSeen && hadActiveStory)
                    Console.Error.WriteLine("[facebook] share.verify: story indicator existed before Share; cannot confirm new publication");
            }
            catch (Exception ex) when (ex is WebDriverException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"[facebook] diagnostics.xml: capture {snapshot} failed: {ex.Message}");
            }

            nextSnapshot += TimeSpan.FromSeconds(2);
            while (nextSnapshot <= observation.Elapsed)
                nextSnapshot += TimeSpan.FromSeconds(2);
        }
        Console.Error.WriteLine($"[facebook] share.unconfirmed: no new story indicator after {observation.Elapsed.TotalSeconds:F1}s");
        return Task.FromResult(new FacebookPreparationResult(
            "facebook_share_unconfirmed",
            hadActiveStory
                ? "Story indicator already existed before Share; new publication cannot be confirmed by this signal."
                : "Story indicator did not appear within 200 seconds after Share.",
            false));
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

public sealed record FacebookPreparationResult(string Status, string Message, bool Published = false);
