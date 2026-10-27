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
        if (string.IsNullOrWhiteSpace(expectedCaption))
            throw new ArgumentException("A non-empty caption is required for publication verification.", nameof(expectedCaption));

        // Share is an explicit publication action. Never retry it on verification timeout.
        ClickWhenReady("com.instagram.android:id/share_button", "Share");
        var directory = Path.Combine(Directory.GetCurrentDirectory(), "diagnostics");
        Directory.CreateDirectory(directory);
        var expected = NormalizeCaption(expectedCaption);
        string lastPath = "";
        var consecutiveMatches = 0;

        // 30 samples, five seconds apart: maximum 150 seconds of monitoring.
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            Thread.Sleep(5000);
            var source = _driver!.PageSource;
            lastPath = Path.GetFullPath(Path.Combine(directory,
                $"instagram_post_share_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{attempt:D2}.xml"));
            File.WriteAllText(lastPath, source, new System.Text.UTF8Encoding(false));

            var document = XDocument.Parse(source);
            var nodes = document.Descendants().ToArray();
            var uploading = nodes.Any(node =>
                (((string?)node.Attribute("text") ?? "") + " " +
                 ((string?)node.Attribute("content-desc") ?? ""))
                .Contains("Sharing to Reels", StringComparison.OrdinalIgnoreCase));

            // Identify the Reel viewer by resource-id, not by account name.
            var reelViewer = nodes.Any(node =>
                string.Equals((string?)node.Attribute("resource-id"),
                    "com.instagram.android:id/clips_media_component", StringComparison.Ordinal));

            // Instagram truncates long captions in the Reel viewer with an ellipsis.
            // Require a meaningful caption prefix rather than matching generic hashtags.
            var captionMatches = nodes.Any(node =>
            {
                var description = (string?)node.Attribute("content-desc") ?? "";
                if (!description.Contains("…", StringComparison.Ordinal) &&
                    !description.Contains("...", StringComparison.Ordinal))
                    return NormalizeCaption(description).Contains(expected, StringComparison.Ordinal);

                var visible = NormalizeCaption(description.Replace("…", "").Replace("...", ""));
                return visible.Length >= Math.Min(12, expected.Length) &&
                       expected.StartsWith(visible, StringComparison.Ordinal);
            });

            if (!uploading && reelViewer && captionMatches)
                consecutiveMatches++;
            else
                consecutiveMatches = 0;

            if (consecutiveMatches >= 3) return (true, lastPath, TryCopyPublishedReelUrl(directory));
        }
        return (false, lastPath, null);
    }

    private string? TryCopyPublishedReelUrl(string directory)
    {
        try
        {
            // This is the share action on an already-published Reel, not the upload Share button.
            ClickWhenReady("com.instagram.android:id/direct_share_button", "Share");
            Thread.Sleep(1200);
            var sheet = XDocument.Parse(_driver!.PageSource);
            var copy = sheet.Descendants().FirstOrDefault(n =>
                string.Equals((string?)n.Attribute("text"), "Copy link", StringComparison.OrdinalIgnoreCase) ||
                string.Equals((string?)n.Attribute("content-desc"), "Copy link", StringComparison.OrdinalIgnoreCase));
            if (copy is null) return null;

            // Match the visible UI element by text or accessibility description.
            var element = _driver.FindElements(OpenQA.Selenium.By.XPath(
                "//*[@text='Copy link' or @content-desc='Copy link']"))
                .FirstOrDefault(e => e.Displayed && e.Enabled);
            if (element is null) return null;
            element.Click();
            Thread.Sleep(500);

            // Reflection keeps this compatible with different Appium .NET client versions.
            var method = _driver.GetType().GetMethod("GetClipboardText", Type.EmptyTypes);
            var clipboard = method?.Invoke(_driver, null) as string;
            if (string.IsNullOrWhiteSpace(clipboard)) return null;
            var match = System.Text.RegularExpressions.Regex.Match(clipboard,
                @"https://(?:www\\.)?instagram\\.com/(?:reel|reels|p)/[A-Za-z0-9_-]+/?(?:\\?[^\\s]*)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            var url = match.Value;
            var output = Path.GetFullPath(Path.Combine(directory, "instagram_published_reel_url.txt"));
            File.WriteAllText(output, url + Environment.NewLine);
            return url;
        }
        catch (Exception)
        {
            // A missing share-sheet action or clipboard permission must not negate a verified post.
            return null;
        }
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
