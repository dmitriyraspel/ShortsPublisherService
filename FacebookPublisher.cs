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
        var candidates = videos.Where(video =>
        {
            var russian = video.Description.StartsWith("Выбрать видео, ", StringComparison.Ordinal);
            var english = video.Description.StartsWith("Select video, ", StringComparison.Ordinal);
            if (!russian && !english) return false;
            var prefix = russian ? "Выбрать видео, " : "Select video, ";
            var datePart = video.Description[prefix.Length..];
            var lastComma = datePart.LastIndexOf(", ", StringComparison.Ordinal);
            if (lastComma < 0) return false;
            var timestamp = datePart[..lastComma];
            var culture = russian ? CultureInfo.GetCultureInfo("ru-RU") : CultureInfo.InvariantCulture;
            if (!DateTime.TryParseExact(timestamp, "MMM dd, yyyy HH:mm",
                    culture, DateTimeStyles.None, out var mediaTime))
                return false;
            var difference = (mediaTime - deviceScanTime).Duration();
            return difference <= TimeSpan.FromMinutes(3);
        }).ToArray();

        Console.Error.WriteLine($"[facebook] gallery.match: {candidates.Length} recent video candidates");
        if (candidates.Length != 1)
            throw new InvalidOperationException(
                $"Facebook video selection is ambiguous: {candidates.Length} candidates. No video selected or published.");

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
        const int snapshotCount = 15; // 30 seconds, one capture every 2 seconds.
        for (var snapshot = 1; snapshot <= snapshotCount; snapshot++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(2000);
            cancellationToken.ThrowIfCancellationRequested();
            var diagnosticFile = Path.Combine(diagnosticDirectory, $"screen_{snapshot:000}.xml");
            try
            {
                File.WriteAllText(diagnosticFile, _driver.PageSource);
                Console.Error.WriteLine($"[facebook] diagnostics.xml: {diagnosticFile}");
            }
            catch (Exception ex) when (ex is WebDriverException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"[facebook] diagnostics.xml: capture {snapshot} failed: {ex.Message}");
            }
        }
        Console.Error.WriteLine("[facebook] share.tap: diagnostics completed; publication confirmation not verified");
        return Task.FromResult(new FacebookPreparationResult(
            "facebook_share_tapped",
            "Tapped Facebook story Share button. Submission was initiated; captured 15 post-share XML snapshots at 2-second intervals in diagnostics/facebook. Publication was not independently verified."));



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

public sealed record FacebookPreparationResult(string Status, string Message);
