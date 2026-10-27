using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;

namespace ShortsPublisher;

public sealed class InstagramPublisher(PublisherOptions options) : IDisposable
{
    private AndroidDriver? _driver;
    private readonly PublisherOptions _options = options;

    /// <summary>Opens Instagram using the existing Appium session.</summary>
    public string OpenInstagram()
    {
        Connect();
        _driver!.ActivateApp("com.instagram.android");
        Thread.Sleep(1500);
        return _driver.PageSource;
    }

    
    public void instagramHome()
    {
        // Кнопка Home в нижней панели Instagram.
        // Центр элемента: x = 108, y = 2274.
        RunAdb("-s", _options.DeviceId, "shell", "input", "tap", "108", "2274");
    }

    // Reuse one Appium session. No adb uiautomator dumps between actions.
    public void instagramCreate() => ClickWhenReady("com.instagram.android:id/action_bar_left_button");

    public void instagramSelectReel() => ClickWhenReady("com.instagram.android:id/cam_dest_clips", "REEL");

    public void WaitForGallery() => WaitForElement("com.instagram.android:id/gallery_recycler_view");

    public string SaveGalleryXml()
    {
        Connect();
        var directory = Path.Combine(Directory.GetCurrentDirectory(), "diagnostics");
        Directory.CreateDirectory(directory);
        var path = Path.GetFullPath(Path.Combine(directory,
            $"instagram_gallery_{DateTime.Now:yyyyMMdd_HHmmss_fff}.xml"));
        var source = _driver!.PageSource;
        System.Xml.Linq.XDocument.Parse(source);
        File.WriteAllText(path, source, new System.Text.UTF8Encoding(false));
        return path;
    }

    // Temporary strategy: choose the first VIDEO thumbnail in the current gallery.
    // The camera tile is excluded. This does not prove file identity.
    public string SelectFirstGalleryVideo()
    {
        Connect();
        var source = _driver!.PageSource;
        var document = XDocument.Parse(source);
        var grid = document.Descendants().FirstOrDefault(n =>
            (string?)n.Attribute("resource-id") == "com.instagram.android:id/gallery_recycler_view")
            ?? throw new InvalidOperationException("Instagram gallery grid is missing.");

        var firstVideo = grid.Descendants().FirstOrDefault(n =>
            (string?)n.Attribute("resource-id") == "com.instagram.android:id/gallery_grid_item_thumbnail" &&
            ((string?)n.Attribute("content-desc") ?? "").Contains("Video thumbnail", StringComparison.OrdinalIgnoreCase));
        if (firstVideo is null)
            throw new InvalidOperationException("No video thumbnail in the visible Instagram gallery. Nothing selected.");

        var bounds = (string?)firstVideo.Attribute("bounds") ?? "";
        var match = System.Text.RegularExpressions.Regex.Match(bounds, @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");
        if (!match.Success) throw new InvalidOperationException("Invalid video thumbnail bounds.");
        var x = (int.Parse(match.Groups[1].Value) + int.Parse(match.Groups[3].Value)) / 2;
        var y = (int.Parse(match.Groups[2].Value) + int.Parse(match.Groups[4].Value)) / 2;
        var thumbs = _driver.FindElements(OpenQA.Selenium.By.Id("com.instagram.android:id/gallery_grid_item_thumbnail"));
        var selected = thumbs.FirstOrDefault(t =>
            t.Displayed && t.Location.X <= x && x < t.Location.X + t.Size.Width &&
            t.Location.Y <= y && y < t.Location.Y + t.Size.Height);
        if (selected is null)
            throw new InvalidOperationException("First video thumbnail is no longer visible. Nothing selected.");

        selected.Click();
        WaitForElement("com.instagram.android:id/clips_right_action_button");
        WaitForElement("com.instagram.android:id/capture_interactive_drawable_container");
        // Move only to the next editing step; never press Share/Publish.
        ClickWhenReady("com.instagram.android:id/clips_right_action_button", "Next");
        Thread.Sleep(1200);
        return SaveGalleryXml();
    }

    public (bool Confirmed, string DiagnosticXmlPath, string? PostUrl) ShareAndMonitorPublication(string expectedCaption)
    {
        // The Instagram UI is the source of truth: "Done posting" confirms publication.
        // Never click Share a second time, even if verification times out.
        ClickWhenReady("com.instagram.android:id/share_button", "Share");
        var directory = Path.Combine(Directory.GetCurrentDirectory(), "diagnostics");
        Directory.CreateDirectory(directory);
        var deadline = Stopwatch.StartNew();
        string lastPath = "";
        var attempt = 0;
        var samples = new System.Collections.Generic.List<string>();
        while (deadline.Elapsed < TimeSpan.FromSeconds(150))
        {
            attempt++;
            var source = _driver!.PageSource;
            lastPath = Path.GetFullPath(Path.Combine(directory,
                $"instagram_post_share_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{attempt:D2}.xml"));
            File.WriteAllText(lastPath, source, new System.Text.UTF8Encoding(false));
            samples.Add(lastPath);
            try
            {
                var nodes = XDocument.Parse(source).Descendants().ToArray();
                var posted = nodes.Any(node =>
                    string.Equals((string?)node.Attribute("resource-id"),
                        "com.instagram.android:id/status_text", StringComparison.Ordinal) &&
                    ((string?)node.Attribute("text") ?? "").Contains("Done posting", StringComparison.OrdinalIgnoreCase));
                if (posted)
                {
                    // The snackbar may disappear quickly. Preserve proof and avoid
                    // interacting with a different Reel shown in the background feed.
                    foreach (var sample in samples.Where(path => path != lastPath))
                    {
                        try { File.Delete(sample); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    }
                    return (true, lastPath, null);
                }
            }
            catch (System.Xml.XmlException)
            {
                // Transient UI hierarchy during animation; keep observing.
            }
            var remaining = TimeSpan.FromSeconds(150) - deadline.Elapsed;
            if (remaining > TimeSpan.Zero)
                Thread.Sleep(remaining < TimeSpan.FromSeconds(2) ? remaining : TimeSpan.FromSeconds(2));
        }
        return (false, lastPath, null);
    }

    /// <summary>Return to Instagram Home and terminate the app after confirmed publication.</summary>
    public void CloseInstagramAfterPublication()
    {
        instagramHome();
        Thread.Sleep(500);
        Connect();
        _driver!.TerminateApp("com.instagram.android");
    }

    private static string NormalizeCaption(string value) =>
        string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim().ToLowerInvariant();

        public string FillReelCaption(string caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
            throw new ArgumentException("Instagram caption must not be empty.", nameof(caption));
        if (caption.Length > 2200)
            throw new ArgumentException("Instagram caption exceeds 2200 characters.", nameof(caption));

        var input = WaitForElement("com.instagram.android:id/caption_input_text_view");
        input.Click();
        input.Clear();
        input.SendKeys(caption);
        var actual = input.GetAttribute("text");
        if (!string.Equals(actual, caption, StringComparison.Ordinal))
            throw new InvalidOperationException("Instagram caption verification failed; not proceeding.");

        // Confirm caption editing (including trailing hashtag suggestions) without publishing.
        ClickWhenReady("com.instagram.android:id/action_bar_button_text", "OK");
        Thread.Sleep(500);
        return SaveGalleryXml();
    }

    private OpenQA.Selenium.Appium.AppiumElement WaitForElement(string resourceId, string? expectedText = null)
    {
        Connect();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        Exception? lastError = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var matches = _driver!.FindElements(OpenQA.Selenium.By.Id(resourceId));
                foreach (var element in matches)
                {
                    if (!element.Displayed || !element.Enabled) continue;
                    if (expectedText is not null &&
                        !string.Equals(element.Text, expectedText, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(element.GetAttribute("content-desc"), expectedText, StringComparison.OrdinalIgnoreCase))
                        continue;
                    return element;
                }
            }
            catch (OpenQA.Selenium.WebDriverException ex)
            {
                lastError = ex;
            }
            Thread.Sleep(750);
        }
        throw new TimeoutException($"Instagram element {resourceId} did not appear within 30 seconds. Last Appium error: {lastError?.Message ?? "none"}");
    }

    private void ClickWhenReady(string resourceId, string? expectedText = null)
    {
        var element = WaitForElement(resourceId, expectedText);
        element.Click();
    }

    private (int ExitCode, string StdOut, string StdErr) DumpWithRetry(string remotePath)
    {
        (int ExitCode, string StdOut, string StdErr) result = default;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            result = RunAdb("-s", _options.DeviceId, "shell", "uiautomator", "dump", remotePath);
            if (result.ExitCode == 0) return result;
            if (attempt < 3) Thread.Sleep(1000 * attempt);
        }
        return result;
    }

    private (int ExitCode, string StdOut, string StdErr) RunAdb(params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Environment.ExpandEnvironmentVariables(_options.AdbPath),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("ADB failed to start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("ADB timed out.");
        }
        Task.WaitAll(stdout, stderr);
        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    private void Connect()
    {
        if (_driver is not null) return;
        var caps = new AppiumOptions();
        caps.PlatformName = "Android";
        caps.AutomationName = "UiAutomator2";
        caps.DeviceName = "Pixel 6a";
        caps.AddAdditionalAppiumOption("udid", _options.DeviceId);
        caps.AddAdditionalAppiumOption("appPackage", "com.instagram.android");
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
