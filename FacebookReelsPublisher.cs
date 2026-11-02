using System.Security.Cryptography;
using System.Xml.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;

namespace ShortsPublisher;

/// <summary>
/// Facebook Reels automation with post-submit XML diagnostics.
/// Submission is not treated as confirmed publication.
/// </summary>
public sealed class FacebookReelsPublisher(PublisherOptions options) : IDisposable
{
    private const string FacebookPackage = "com.facebook.katana";
    private AndroidDriver? _driver;

    public FacebookReelsPreparationResult OpenReelCreation(string? filePath, string? caption, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("Facebook Reel requires an existing --file video.", filePath);
        if (!new[] { ".mp4", ".mov", ".m4v" }.Contains(Path.GetExtension(filePath), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unsupported Facebook Reel video extension.");
        if (new FileInfo(filePath).Length == 0)
            throw new InvalidOperationException("Facebook Reel video is empty.");

        using var input = File.OpenRead(filePath);
        var sha = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        var remoteDirectory = options.AndroidMediaDirectory.TrimEnd('/');
        var remotePath = $"{remoteDirectory}/fb_reel_{sha[..16]}_{Path.GetFileName(filePath)}";
        var adb = new AdbClient(options.AdbPath);
        var devices = adb.DevicesAsync(cancellationToken).GetAwaiter().GetResult();
        if (!devices.Split('\n').Any(line => line.Trim().StartsWith(options.DeviceId + "\tdevice", StringComparison.Ordinal)))
            throw new InvalidOperationException("Configured Android device is not ready.");
        adb.ShellAsync(options.DeviceId, cancellationToken, "mkdir", "-p", remoteDirectory).GetAwaiter().GetResult();
        Console.Error.WriteLine("[facebook-reel] media.push: copying video to Android");
        adb.PushAsync(options.DeviceId, filePath, remotePath, cancellationToken).GetAwaiter().GetResult();
        var remoteHashOutput = adb.ShellAsync(options.DeviceId, cancellationToken, "sha256sum", remotePath).GetAwaiter().GetResult();
        var remoteHash = remoteHashOutput.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.Equals(sha, remoteHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Facebook Reel Android SHA-256 mismatch.");
        Console.Error.WriteLine("[facebook-reel] media.verify: SHA-256 matches");
        adb.ShellAsync(options.DeviceId, cancellationToken, "am", "broadcast",
            "-a", "android.intent.action.MEDIA_SCANNER_SCAN_FILE", "-d", $"file://{remotePath}").GetAwaiter().GetResult();
        Console.Error.WriteLine("[facebook-reel] media.scan: requested");
        Console.Error.WriteLine("[facebook-reel] app.open: connecting to Facebook");
        var caps = new AppiumOptions();
        caps.PlatformName = "Android";
        caps.AutomationName = "UiAutomator2";
        caps.DeviceName = "Pixel 6a";
        caps.AddAdditionalAppiumOption("udid", options.DeviceId);
        caps.AddAdditionalAppiumOption("appPackage", FacebookPackage);
        caps.AddAdditionalAppiumOption("noReset", true);
        caps.AddAdditionalAppiumOption("newCommandTimeout", 180);
        _driver = new AndroidDriver(new Uri(options.AppiumServer), caps, TimeSpan.FromMinutes(3));
        _driver.ActivateApp(FacebookPackage);
        Console.Error.WriteLine("[facebook-reel] app.open: ready");

        // Facebook can reopen on Reels or a profile. Navigate to Home first:
        // the Home header's Create button is more reliable than the Reels header.
        var home = _driver.FindElements(By.XPath(
            "//*[@clickable='true' and (starts-with(@content-desc,'Главная, вкладка') or starts-with(@content-desc,'Home, tab'))]"))
            .FirstOrDefault(element => element.Displayed && element.Enabled);
        if (home is not null)
        {
            home.Click();
            Console.Error.WriteLine("[facebook-reel] navigation.home: tapped Home tab");
        }

        var createDeadline = DateTime.UtcNow.AddSeconds(15);
        AppiumElement? create = null;
        while (DateTime.UtcNow < createDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            create = _driver.FindElements(By.XPath(
                "//*[@clickable='true' and (starts-with(@content-desc,'Создать:') or starts-with(@content-desc,'Create,'))]"))
                .FirstOrDefault(element => element.Displayed && element.Enabled);
            if (create is not null) break;
            Thread.Sleep(400);
        }
        if (create is null)
            return Failure("facebook_reel_create_button_not_found",
                "Home Create button was not found. No publication attempted.");

        create.Click();
        Console.Error.WriteLine("[facebook-reel] create.menu: tapped Home Create; looking for Reel option");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reel = _driver.FindElements(By.XPath(
                "//*[@clickable='true' and (@content-desc='Видео Reels' or @content-desc='Reel' or @content-desc='Создать видео Reels' or @content-desc='Create reel' or @text='Видео Reels' or @text='Reel' or @text='Создать видео Reels' or @text='Create reel')]"))
                .FirstOrDefault(element => element.Displayed && element.Enabled);
            if (reel is not null)
            {
                reel.Click();
                Console.Error.WriteLine("[facebook-reel] create.open: tapped Reel option");
                var galleryDeadline = DateTime.UtcNow.AddSeconds(20);
                // Facebook may offer to resume unsaved Reel edits. For this workflow
                // always discard those changes and start a fresh Reel.
                var resetHandled = false;
                while (DateTime.UtcNow < galleryDeadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!resetHandled && _driver.FindElements(By.XPath(
                        "//*[contains(@text,'Продолжить редактирование последнего видео') or contains(@text,'Continue editing your last reel')]"))
                        .Any(element => element.Displayed))
                    {
                        var reset = _driver.FindElements(By.XPath(
                            "//*[@resource-id='android:id/button3' and (@text='СБРОСИТЬ' or @text='RESET' or @text='Discard')]"))
                            .FirstOrDefault(element => element.Displayed && element.Enabled);
                        if (reset is null)
                            return Failure("facebook_reel_reset_button_not_found",
                                "Previous Reel edit dialog found, but Reset button is unavailable. No publication attempted.");
                        reset.Click();
                        resetHandled = true;
                        Console.Error.WriteLine("[facebook-reel] create.reset: discarded unsaved previous Reel edits");
                    }
                    if (_driver.FindElements(By.XPath(
                        "//*[@text='Создание видео Reels' or @content-desc='Создание видео Reels' or @text='Create reel' or @content-desc='Create reel']"))
                        .Any(element => element.Displayed))
                    {
                        Console.Error.WriteLine("[facebook-reel] gallery.open: selecting newest video");
                        var videoDeadline = DateTime.UtcNow.AddSeconds(15);
                        while (DateTime.UtcNow < videoDeadline)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var videos = _driver.FindElements(By.XPath(
                                "//*[@clickable='true' and (starts-with(@content-desc,'Создать видео') or starts-with(@content-desc,'Create reel')) and contains(@content-desc,'Видео, объект 1')]"));
                            var first = videos.FirstOrDefault(element => element.Displayed && element.Enabled);
                            if (first is not null)
                            {
                                first.Click();
                                Console.Error.WriteLine("[facebook-reel] gallery.select: tapped newest video");
                                var nextDeadline = DateTime.UtcNow.AddSeconds(20);
                                while (DateTime.UtcNow < nextDeadline)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    var next = _driver.FindElements(By.XPath(
                                        "//*[@clickable='true' and (@content-desc='Далее' or @content-desc='Next' or @text='Далее' or @text='Next')]"))
                                        .FirstOrDefault(element => element.Displayed && element.Enabled);
                                    if (next is not null)
                                    {
                                        next.Click();
                                        Console.Error.WriteLine("[facebook-reel] editor.next: tapped Next");
                                        var captionDeadline = DateTime.UtcNow.AddSeconds(35);
                                        while (DateTime.UtcNow < captionDeadline)
                                        {
                                            cancellationToken.ThrowIfCancellationRequested();
                                            var captionField = _driver.FindElements(By.XPath(
                                                "//*[@class='android.widget.AutoCompleteTextView' and (contains(@content-desc,'описание к своему видео Reels') or contains(@text,'описание к своему видео Reels') or contains(@content-desc,'description') or contains(@text,'description'))]"))
                                                .FirstOrDefault(element => element.Displayed && element.Enabled);
                                            if (captionField is not null)
                                            {
                                                if (!string.IsNullOrWhiteSpace(caption))
                                                {
                                                    captionField.Click();
                                                    captionField.SendKeys(caption);
                                                    Console.Error.WriteLine("[facebook-reel] caption.enter: description entered");
                                                }
                                                else
                                                {
                                                    Console.Error.WriteLine("[facebook-reel] caption.skip: no caption provided");
                                                }
                                                // Hide the software keyboard without leaving the Reel settings screen.
                                                if (!string.IsNullOrWhiteSpace(caption))
                                                {
                                                    adb.ShellAsync(options.DeviceId, cancellationToken,
                                                        "input", "keyevent", "4").GetAwaiter().GetResult();
                                                    Console.Error.WriteLine("[facebook-reel] keyboard.hide: Android Back sent");
                                                }
                                                var shareDeadline = DateTime.UtcNow.AddSeconds(15);
                                                AppiumElement? share = null;
                                                while (DateTime.UtcNow < shareDeadline)
                                                {
                                                    cancellationToken.ThrowIfCancellationRequested();
                                                    share = _driver.FindElements(By.XPath(
                                                        "//*[@class='android.widget.Button' and @clickable='true' and (@content-desc='Поделиться сейчас' or @content-desc='Share now' or @text='Поделиться сейчас' or @text='Share now')]"))
                                                        .FirstOrDefault(element => element.Displayed && element.Enabled);
                                                    if (share is not null) break;
                                                    Thread.Sleep(400);
                                                }
                                                if (share is null)
                                                    return Failure("facebook_reel_share_button_not_found",
                                                        "Caption processed but Share now button was not found. No publication attempted.");

                                                share.Click();
                                                Console.Error.WriteLine("[facebook-reel] share.tap: tapped Share now");
                                                var diagnosticDirectory = Path.Combine("diagnostics", "facebook-reels",
                                                    $"post_share_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}");
                                                Directory.CreateDirectory(diagnosticDirectory);
                                                var sampleCount = 0;
                                                var diagnosticErrors = 0;
                                                var confirmedSamples = 0;
                                                // Capture the changing UI for analysis. Do not infer success from
                                                // the click or from the disappearance of the editor.
                                                for (var sample = 0; sample < 100; sample++)
                                                {
                                                    cancellationToken.ThrowIfCancellationRequested();
                                                    var xmlPath = Path.Combine(diagnosticDirectory,
                                                        $"screen_{sample:000}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}.xml");
                                                    try
                                                    {
                                                        var pageXml = _driver.PageSource;
                                                        File.WriteAllText(xmlPath, pageXml);
                                                        sampleCount++;
                                                        // A matching caption in the feed together with the Reel type
                                                        // is stronger evidence than the upload banner disappearing.
                                                        if (!string.IsNullOrWhiteSpace(caption))
                                                        {
                                                            var nodes = XDocument.Parse(pageXml).Descendants();
                                                            var captionVisible = nodes.Any(n =>
                                                                string.Equals((string?)n.Attribute("text"), caption, StringComparison.Ordinal) ||
                                                                string.Equals((string?)n.Attribute("content-desc"), caption, StringComparison.Ordinal));
                                                            var reelVisible = nodes.Any(n =>
                                                                string.Equals((string?)n.Attribute("text"), "Видео Reels", StringComparison.Ordinal) ||
                                                                string.Equals((string?)n.Attribute("content-desc"), "Видео Reels", StringComparison.Ordinal) ||
                                                                string.Equals((string?)n.Attribute("text"), "Reel", StringComparison.Ordinal));
                                                            var uploading = pageXml.Contains("Завершение загрузки видео Reels", StringComparison.Ordinal);
                                                            confirmedSamples = captionVisible && reelVisible && !uploading ? confirmedSamples + 1 : 0;
                                                            if (confirmedSamples >= 2)
                                                            {
                                                                Console.Error.WriteLine("[facebook-reel] share.confirmed: matching Reel visible in feed");
                                                                return new FacebookReelsPreparationResult(
                                                                    "facebook_reel_published",
                                                                    $"Matching Reel caption and Reel label observed in two consecutive XML snapshots. Diagnostics: {Path.GetFullPath(diagnosticDirectory)}.");
                                                            }
                                                        }
                                                        Console.Error.WriteLine($"[facebook-reel] share.xml: {xmlPath}");
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        diagnosticErrors++;
                                                        Console.Error.WriteLine($"[facebook-reel] share.xml warning: {ex.Message}");
                                                    }
                                                    if (sample < 99) Thread.Sleep(2000);
                                                }
                                                return new FacebookReelsPreparationResult(
                                                    "facebook_reel_share_submitted_unconfirmed",
                                                    $"Share now tapped; publication NOT confirmed. Saved {sampleCount} XML snapshots (errors: {diagnosticErrors}) to {Path.GetFullPath(diagnosticDirectory)}.");
                                            }
                                            Thread.Sleep(500);
                                        }
                                        return Failure("facebook_reel_caption_field_not_found",
                                            "Tapped Next, but Reel caption field did not appear within 35 seconds. No publication attempted.");
                                    }
                                    Thread.Sleep(400);
                                }
                                return Failure("facebook_reel_next_not_found",
                                    "Video selected but Next button was not found. No publication attempted.");
                            }
                            Thread.Sleep(400);
                        }
                        return Failure("facebook_reel_video_not_found",
                            "Reels gallery opened but first video tile was not found. No publication attempted.");
                    }
                    Thread.Sleep(400);
                }
                return Failure("facebook_reel_gallery_not_found",
                    "Reel option tapped, but creation gallery not confirmed. No publication attempted.");
            }
            Thread.Sleep(400);
        }

        return Failure("facebook_reel_option_not_found",
            "Home Create menu opened, but Reel option was not found. No publication attempted.");
    }

    /// <summary>Copy the link of an already visible Facebook Reel. Never starts a publication.</summary>
    public FacebookReelsPreparationResult CopyVisibleReelLink(string? caption = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var caps = new AppiumOptions();
        caps.PlatformName = "Android";
        caps.AutomationName = "UiAutomator2";
        caps.DeviceName = "Pixel 6a";
        caps.AddAdditionalAppiumOption("udid", options.DeviceId);
        caps.AddAdditionalAppiumOption("appPackage", FacebookPackage);
        caps.AddAdditionalAppiumOption("noReset", true);
        caps.AddAdditionalAppiumOption("newCommandTimeout", 180);
        _driver = new AndroidDriver(new Uri(options.AppiumServer), caps, TimeSpan.FromMinutes(3));
        _driver.ActivateApp(FacebookPackage);
        Console.Error.WriteLine("[facebook-reel] link.open: connected to existing Facebook screen");

        // The user can leave the already published Reel visible in the feed.
        // If a caption was supplied, refuse to share an unrelated visible Reel.
        if (!string.IsNullOrWhiteSpace(caption))
        {
            var captionFound = _driver.FindElements(By.XPath(
                "//*[@text=" + XPathLiteral(caption) + " or @content-desc=" + XPathLiteral(caption) + "]"))
                .Any(e => e.Displayed);
            if (!captionFound)
                return new FacebookReelsPreparationResult("facebook_reel_link_caption_not_found",
                    "Requested caption is not visible. Open the published Reel in Facebook and retry; no publication attempted.");
        }

        // Facebook renders the Share label as a non-clickable child of an unnamed button.
        var share = _driver.FindElements(By.XPath(
            "//*[(@text='Поделиться' or @content-desc='Поделиться' or @text='Share' or @content-desc='Share')]/ancestor-or-self::*[@clickable='true'][1]"))
            .FirstOrDefault(e => e.Displayed && e.Enabled);
        if (share is not null)
        {
            share.Click();
            Console.Error.WriteLine("[facebook-reel] link.share: opened Reel share sheet");
        }

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var copy = _driver.FindElements(By.XPath(
                "//*[@clickable='true' and (@content-desc='Копировать ссылку' or @content-desc='Copy link' or @text='Копировать ссылку' or @text='Copy link')]"))
                .FirstOrDefault(e => e.Displayed && e.Enabled);
            if (copy is not null)
            {
                copy.Click();
                Console.Error.WriteLine("[facebook-reel] link.copy: tapped Copy link");
                try
                {
                    Thread.Sleep(500);
                    var clipboard = _driver.GetClipboardText()?.Trim();
                    if (Uri.TryCreate(clipboard, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
                        (uri.Host.Equals("facebook.com", StringComparison.OrdinalIgnoreCase) ||
                         uri.Host.EndsWith(".facebook.com", StringComparison.OrdinalIgnoreCase) ||
                         uri.Host.Equals("fb.watch", StringComparison.OrdinalIgnoreCase)))
                    {
                        Console.Error.WriteLine("[facebook-reel] link.clipboard: Facebook URL read");
                        try
                        {
                            _driver.Navigate().Back();
                            Console.Error.WriteLine("[facebook-reel] link.close: dismissed share sheet");
                        }
                        catch (Exception closeEx)
                        {
                            Console.Error.WriteLine($"[facebook-reel] link.close warning: {closeEx.Message}");
                        }
                        return new FacebookReelsPreparationResult("facebook_reel_link_read",
                            "Copied existing Reel URL from Android clipboard; no publication attempted.", clipboard);
                    }
                    return new FacebookReelsPreparationResult("facebook_reel_clipboard_invalid",
                        "Copy link tapped, but Android clipboard did not contain a recognized Facebook URL.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[facebook-reel] link.clipboard warning: {ex.Message}");
                    return new FacebookReelsPreparationResult("facebook_reel_clipboard_unavailable",
                        "Copy link tapped, but Appium could not read the Android clipboard: " + ex.Message);
                }
            }
            Thread.Sleep(400);
        }
        return new FacebookReelsPreparationResult("facebook_reel_copy_link_not_found",
            "Copy link button was not found. Open the published Reel share sheet and retry; no publication attempted.");
    }

    private static string XPathLiteral(string value)
    {
        if (!value.Contains('\'')) return "'" + value + "'";
        if (!value.Contains('"')) return "\"" + value + "\"";
        return "concat(" + string.Join(", \"'\", ", value.Split('\'').Select(part => "'" + part + "'")) + ")";
    }

    private FacebookReelsPreparationResult Failure(string status, string message)
    {
        string? diagnosticPath = null;
        try
        {
            var directory = Path.Combine("diagnostics", "facebook-reels");
            Directory.CreateDirectory(directory);
            diagnosticPath = Path.Combine(directory,
                $"navigation_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}.xml");
            File.WriteAllText(diagnosticPath, _driver!.PageSource);
            Console.Error.WriteLine($"[facebook-reel] diagnostics.xml: {diagnosticPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[facebook-reel] diagnostics.xml warning: {ex.Message}");
        }
        return new FacebookReelsPreparationResult(status,
            $"{message} Diagnostic XML: {diagnosticPath ?? "<unavailable>"}");
    }

    public void Dispose()
    {
        try { _driver?.Quit(); } catch { }
        _driver?.Dispose();
    }
}

public sealed record FacebookReelsPreparationResult(string Status, string Message, string? PublicationUrl = null);
