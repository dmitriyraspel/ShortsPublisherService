using System.Security.Cryptography;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;

namespace ShortsPublisher;

/// <summary>
/// Incremental Facebook UI automation. Opens Facebook, navigates to Profile, and taps Add to story.
/// No media upload or publication is performed.
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
                "//*[@class='android.app.ActionBar$Tab' and starts-with(@content-desc,'Profile, tab ')]"))
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
                "//*[@class='android.app.ActionBar$Tab' and starts-with(@content-desc,'Profile, tab ') and @selected='true']"));
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
                "//*[@class='android.widget.Button' and @content-desc='Add to story' and @clickable='true']"))
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
                "//*[@class='android.widget.Spinner' and starts-with(@content-desc,'Select album, ')]")).Count > 0)
                break;
            Thread.Sleep(500);
        }
        var videos = _driver.FindElements(By.XPath(
            "//*[@class='android.widget.Button' and starts-with(@content-desc,'Select video, ')]"))
            .Where(element => element.Displayed)
            .Select(element => element.GetAttribute("content-desc") ?? "")
            .ToArray();
        Console.Error.WriteLine($"[facebook] gallery.inspect: {videos.Length} visible video tiles");
        foreach (var video in videos)
            Console.Error.WriteLine($"[facebook] gallery.video: {video}");
        Console.Error.WriteLine($"[facebook] gallery.inspect: Android media name={remoteName}; no video selected");
        return Task.FromResult(new FacebookPreparationResult(
            "facebook_gallery_inspected",
            $"Android SHA-256 verified. Visible videos: {videos.Length}. No video selected or published. Android media: {remoteName}."));
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
