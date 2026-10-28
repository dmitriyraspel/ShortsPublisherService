using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ShortsPublisher;

/// <summary>
/// TikTok Android UI automation entry point.
/// UI selectors and publishing steps will be added after inspecting --diagnose-ui XML.
/// </summary>
public sealed class TikTokPublisher(PublisherOptions options)
{
    private const string TikTokPackage = "com.zhiliaoapp.musically";

    /// <summary>Launch TikTok on the configured Android device without publishing.</summary>
    public async Task OpenTikTokAsync(CancellationToken cancellationToken = default)
    {
        Console.Error.WriteLine("[tiktok] app.open: launching TikTok");
        var adb = new AdbClient(options.AdbPath);
        var devices = await adb.DevicesAsync(cancellationToken);
        if (!devices.Split('\n').Any(line =>
                line.Trim().StartsWith(options.DeviceId + "\tdevice", StringComparison.Ordinal)))
            throw new InvalidOperationException($"Android device '{options.DeviceId}' is not ready.");

        // Monkey launches the package's launcher activity without requiring its activity name.
        var output = await adb.ShellAsync(options.DeviceId, cancellationToken,
            "monkey", "-p", TikTokPackage, "-c", "android.intent.category.LAUNCHER", "1");
        if (output.Contains("No activities found", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("monkey aborted", StringComparison.OrdinalIgnoreCase) ||
            !output.Contains("Events injected: 1", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Could not launch TikTok ({TikTokPackage}). Check installation. ADB output: {output}");
        Console.Error.WriteLine("[tiktok] app.open: launch command completed");
    }

    private async Task<XDocument> ReadUiAsync(CancellationToken ct)
    {
        var adb = new AdbClient(options.AdbPath);
        const string dumpPath = "/sdcard/window_tiktok_guard.xml";
        await adb.ShellAsync(options.DeviceId, ct, "uiautomator", "dump", dumpPath);
        var xml = await adb.ShellAsync(options.DeviceId, ct, "cat", dumpPath);
        var start = xml.IndexOf("<?xml", StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("TikTok UI dump contains no XML.");
        return XDocument.Parse(xml[start..]);
    }

    private static bool HasUi(XDocument xml, string attribute, string value) =>
        xml.Descendants("node").Any(n => string.Equals((string?)n.Attribute(attribute), value, StringComparison.Ordinal));

    private async Task RequireUiAsync(string stage, Func<XDocument, bool> predicate, CancellationToken ct)
    {
        const int maxAttempts = 8;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            Console.Error.WriteLine($"[tiktok] ui.guard.{stage}: attempt {attempt}/{maxAttempts}");
            XDocument? xml = null;
            try
            {
                xml = await ReadUiAsync(ct);
                var nodes = xml.Descendants("node").ToList();
                var markers = nodes.Where(n =>
                        !string.IsNullOrEmpty((string?)n.Attribute("text")) ||
                        !string.IsNullOrEmpty((string?)n.Attribute("content-desc")))
                    .Select(n => $"{(string?)n.Attribute("resource-id")}:{(string?)n.Attribute("text")}:{(string?)n.Attribute("content-desc")}")
                    .Take(12);
                Console.Error.WriteLine($"[tiktok] ui.guard.{stage}: nodes={nodes.Count}, markers=[{string.Join(" | ", markers)}]");
                if (predicate(xml))
                {
                    Console.Error.WriteLine($"[tiktok] ui.guard.{stage}: verified on attempt {attempt}");
                    return;
                }
                Console.Error.WriteLine($"[tiktok] ui.guard.{stage}: expected control absent");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"[tiktok] ui.guard.{stage}: dump failed: {ex.Message}");
            }

            if (attempt == maxAttempts)
            {
                if (xml is not null)
                {
                    var directory = Path.Combine(Directory.GetCurrentDirectory(), "diagnostics");
                    Directory.CreateDirectory(directory);
                    var safeStage = new string(stage.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
                    var path = Path.GetFullPath(Path.Combine(directory,
                        $"tiktok_guard_{safeStage}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.xml"));
                    File.WriteAllText(path, xml.ToString(), new System.Text.UTF8Encoding(false));
                    Console.Error.WriteLine($"[tiktok] ui.guard.{stage}: saved XML {path}");
                }
                throw new InvalidOperationException(
                    $"TikTok UI guard failed at {stage} after {maxAttempts} attempts. No tap performed. Check diagnostics XML.");
            }
            await Task.Delay(1000, ct);
        }
    }

    /// <summary>Tap the actual Create navigation button using its current XML bounds.</summary>
    public async Task OpenCreateAsync(CancellationToken cancellationToken = default)
    {
        const string uploadId = "com.zhiliaoapp.musically:id/upload_hot_area";
        Console.Error.WriteLine("[tiktok] create.open: locating Create in current UI");
        var adb = new AdbClient(options.AdbPath);
        await RequireUiAsync("home Create", x => x.Descendants("node").Any(n =>
            (string?)n.Attribute("content-desc") == "Create" &&
            (string?)n.Attribute("clickable") == "true" &&
            (string?)n.Attribute("enabled") == "true" &&
            TryGetBoundsCenter((string?)n.Attribute("bounds"), out _, out _)), cancellationToken);

        // Re-read immediately before tapping; stale coordinates can open For You instead.
        var xml = await ReadUiAsync(cancellationToken);
        var candidates = xml.Descendants("node")
            .Where(n => (string?)n.Attribute("content-desc") == "Create" &&
                        (string?)n.Attribute("clickable") == "true" &&
                        (string?)n.Attribute("enabled") == "true")
            .ToList();
        if (candidates.Count != 1 ||
            !TryGetBoundsCenter((string?)candidates[0].Attribute("bounds"), out var x, out var y))
            throw new InvalidOperationException($"Expected exactly one clickable Create button, found {candidates.Count}. No tap performed.");
        Console.Error.WriteLine($"[tiktok] create.open: Create bounds={(string?)candidates[0].Attribute("bounds")}; tap=({x},{y})");
        await adb.ShellAsync(options.DeviceId, cancellationToken, "input", "tap", x.ToString(), y.ToString());
        await RequireUiAsync("create screen after tap", screen => HasUi(screen, "resource-id", uploadId), cancellationToken);
        Console.Error.WriteLine("[tiktok] create.open: Create screen verified");
    }

    private static bool TryGetBoundsCenter(string? bounds, out int x, out int y)
    {
        x = y = 0;
        if (string.IsNullOrEmpty(bounds)) return false;
        var match = System.Text.RegularExpressions.Regex.Match(bounds,
            @"^\\[(\\d+),(\\d+)\\]\\[(\\d+),(\\d+)\\]$");
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, out var left) ||
            !int.TryParse(match.Groups[2].Value, out var top) ||
            !int.TryParse(match.Groups[3].Value, out var right) ||
            !int.TryParse(match.Groups[4].Value, out var bottom) ||
            right <= left || bottom <= top) return false;
        x = left + (right - left) / 2;
        y = top + (bottom - top) / 2;
        return true;
    }

    /// <summary>Open the media gallery from TikTok's Create screen.</summary>
    public async Task OpenGalleryAsync(CancellationToken cancellationToken = default)
    {
        Console.Error.WriteLine("[tiktok] gallery.open: tapping bottom-left Upload button");
        var adb = new AdbClient(options.AdbPath);
        // Diagnosed UI: com.zhiliaoapp.musically:id/upload_hot_area,
        // bounds=[0,2135][179,2314] on the 1080x2400 emulator.
        await RequireUiAsync("create Upload", x => HasUi(x, "resource-id", "com.zhiliaoapp.musically:id/upload_hot_area"), cancellationToken);
        await adb.ShellAsync(options.DeviceId, cancellationToken, "input", "tap", "90", "2225");
        Console.Error.WriteLine("[tiktok] gallery.open: tap completed");
    }

    /// <summary>Select the Videos filter in the media picker; do not select a tile.</summary>
    public async Task SelectVideosTabAsync(CancellationToken cancellationToken = default)
    {
        Console.Error.WriteLine("[tiktok] gallery.filter: selecting Videos tab");
        var adb = new AdbClient(options.AdbPath);
        // UI XML: content-desc="Videos", bounds=[344,258][689,376].
        // Coordinates apply to the diagnosed 1080x2400 emulator only.
        await RequireUiAsync("gallery Videos tab", x => HasUi(x, "content-desc", "Videos"), cancellationToken);
        await adb.ShellAsync(options.DeviceId, cancellationToken, "input", "tap", "516", "317");
        Console.Error.WriteLine("[tiktok] gallery.filter: tap completed; inspect --diagnose-ui");
    }

    /// <summary>
    /// Select the first video tile in the Videos gallery, as in the Instagram flow.
    /// This does not verify the file identity; it assumes newest media appears first.
    /// </summary>
    public async Task SelectFirstGalleryVideoAsync(CancellationToken cancellationToken = default)
    {
        Console.Error.WriteLine("[tiktok] gallery.select: tapping first video thumbnail (position-based)");
        var adb = new AdbClient(options.AdbPath);
        // Diagnosed first tile bounds=[5,381][359,738]. Avoid its top-right checkbox.
        await RequireUiAsync("Videos gallery selected", x => x.Descendants("node").Any(n => (string?)n.Attribute("content-desc") == "Videos" && (string?)n.Attribute("selected") == "true") && HasUi(x, "text", "Next"), cancellationToken);
        await adb.ShellAsync(options.DeviceId, cancellationToken, "input", "tap", "150", "560");
        Console.Error.WriteLine("[tiktok] gallery.select: tap completed");
    }

    /// <summary>Press Next after selecting a gallery video. Does not publish.</summary>
    public async Task PressNextAsync(CancellationToken cancellationToken = default)
    {
        Console.Error.WriteLine("[tiktok] gallery.next: tapping Next");
        var adb = new AdbClient(options.AdbPath);
        // Diagnosed Next button id=y3s, bounds=[550,2179][1048,2295].
        await RequireUiAsync("gallery Next", x => x.Descendants("node").Any(n => (string?)n.Attribute("content-desc") == "Videos" && (string?)n.Attribute("selected") == "true") && HasUi(x, "text", "Next") && !x.Descendants("node").Any(n => (string?)n.Attribute("content-desc") == "Inbox" && (string?)n.Attribute("selected") == "true"), cancellationToken);
        await adb.ShellAsync(options.DeviceId, cancellationToken, "input", "tap", "800", "2237");
        Console.Error.WriteLine("[tiktok] gallery.next: tap completed; publication not attempted");
    }

    /// <summary>Advance from the video editing screen to the posting details screen; never publish.</summary>
    public async Task PressEditorNextAsync(CancellationToken cancellationToken = default)
    {
        Console.Error.WriteLine("[tiktok] editor.next: checking editing screen");
        await RequireUiAsync("editor Next", x =>
            HasUi(x, "resource-id", "com.zhiliaoapp.musically:id/video_record_gesture_layout") &&
            HasUi(x, "resource-id", "com.zhiliaoapp.musically:id/q25") &&
            HasUi(x, "resource-id", "com.zhiliaoapp.musically:id/q27") &&
            HasUi(x, "text", "Next"), cancellationToken);
        var adb = new AdbClient(options.AdbPath);
        // Editor Next clickable container: bounds=[550,2192][1048,2308].
        await adb.ShellAsync(options.DeviceId, cancellationToken, "input", "tap", "799", "2250");
        Console.Error.WriteLine("[tiktok] editor.next: tap completed; Post not pressed");
    }

    /// <summary>Enter and verify the caption on the posting screen; never press Post.</summary>
    public async Task FillCaptionAsync(string caption, CancellationToken cancellationToken = default)
    {
        const string fieldId = "com.zhiliaoapp.musically:id/h_n";
        const string postId = "com.zhiliaoapp.musically:id/tl7";
        if (string.IsNullOrWhiteSpace(caption))
            throw new ArgumentException("Caption must not be empty.", nameof(caption));
        // ADB input text is reliable here only for simple ASCII text. Fail closed
        // rather than silently publishing a corrupted Unicode/special-character caption.
        if (caption.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_' || ch == '.' || ch == ',' || ch == '!' || ch == '?' || ch == '#')))
            throw new NotSupportedException("TikTok caption via ADB currently supports ASCII letters, digits, spaces and - _ . , ! ? # only.");

        Console.Error.WriteLine("[tiktok] caption.fill: locating description field");
        await RequireUiAsync("posting caption field", x =>
            x.Descendants("node").Any(n =>
                (string?)n.Attribute("resource-id") == fieldId &&
                (string?)n.Attribute("class") == "android.widget.EditText") &&
            HasUi(x, "resource-id", postId), cancellationToken);

        var before = await ReadUiAsync(cancellationToken);
        var field = before.Descendants("node").First(n => (string?)n.Attribute("resource-id") == fieldId);
        var current = (string?)field.Attribute("text") ?? "";
        if (current != "Add description..." && !string.IsNullOrEmpty(current))
            throw new InvalidOperationException("Caption field already contains text; refusing to overwrite.");

        var adb = new AdbClient(options.AdbPath);
        // Field bounds=[42,270][673,677] in diagnosed 1080x2400 UI.
        await adb.ShellAsync(options.DeviceId, cancellationToken, "input", "tap", "300", "420");
        // adb input text uses %s to represent spaces.
        var encoded = caption.Replace(" ", "%s");
        await adb.ShellAsync(options.DeviceId, cancellationToken, "input", "text", encoded);
        await Task.Delay(400, cancellationToken);

        var after = await ReadUiAsync(cancellationToken);
        var updated = after.Descendants("node").FirstOrDefault(n => (string?)n.Attribute("resource-id") == fieldId);
        if ((string?)updated?.Attribute("text") != caption)
            throw new InvalidOperationException("Caption verification failed; Post was not pressed. Run --diagnose-ui.");
        Console.Error.WriteLine("[tiktok] caption.suggestions: finishing last hashtag with a space");
        await adb.ShellAsync(options.DeviceId, cancellationToken, "input", "text", "%s");
        await Task.Delay(350, cancellationToken);
        await RequireUiAsync("hashtag suggestions dismissed", x =>
            x.Descendants("node").Any(n =>
                (string?)n.Attribute("resource-id") == fieldId &&
                ((string?)n.Attribute("text") ?? "").Trim() == caption.Trim()) &&
            !HasUi(x, "resource-id", "com.zhiliaoapp.musically:id/k0y") &&
            !HasUi(x, "resource-id", "com.zhiliaoapp.musically:id/k0x"),
            cancellationToken);

        Console.Error.WriteLine("[tiktok] caption.keyboard: hiding keyboard with BACK");
        await adb.ShellAsync(options.DeviceId, cancellationToken, "input", "keyevent", "4");
        await RequireUiAsync("posting screen after keyboard dismissal", x =>
            x.Descendants("node").Any(n =>
                (string?)n.Attribute("resource-id") == fieldId &&
                ((string?)n.Attribute("text") ?? "").Trim() == caption.Trim()) &&
            !HasUi(x, "resource-id", "com.zhiliaoapp.musically:id/k0y") &&
            HasUi(x, "resource-id", "com.zhiliaoapp.musically:id/tl7"),
            cancellationToken);
        Console.Error.WriteLine("[tiktok] caption.ready: text verified, suggestions dismissed, keyboard back requested; Post not pressed");
    }

    /// <summary>Press the bottom Post button once and capture UI XML every two seconds for diagnostics.</summary>
    public async Task PressPostAndDiagnoseAsync(CancellationToken cancellationToken = default)
    {
        const string postId = "com.zhiliaoapp.musically:id/tl7";
        const string captionId = "com.zhiliaoapp.musically:id/h_n";
        Console.Error.WriteLine("[tiktok] post.guard: verifying bottom Post button and caption");
        await RequireUiAsync("bottom Post ready", x =>
            x.Descendants("node").Any(n =>
                (string?)n.Attribute("resource-id") == postId &&
                (string?)n.Attribute("text") == "Post" &&
                (string?)n.Attribute("enabled") == "true" &&
                (string?)n.Attribute("bounds") == "[550,2179][1048,2305]") &&
            HasUi(x, "resource-id", captionId), cancellationToken);

        var directory = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(), "diagnostics",
            $"tiktok_post_{DateTime.Now:yyyyMMdd_HHmmss}"));
        Directory.CreateDirectory(directory);
        var adb = new AdbClient(options.AdbPath);
        Console.Error.WriteLine($"[tiktok] post.diagnostics: {directory}");
        // Capture baseline before the irreversible action.
        File.WriteAllText(Path.Combine(directory, "ui_00_before_post.xml"),
            (await ReadUiAsync(cancellationToken)).ToString(),
            new System.Text.UTF8Encoding(false));

        Console.Error.WriteLine("[tiktok] post.tap: pressing bottom Post ONCE at (799,2242)");
        await adb.ShellAsync(options.DeviceId, cancellationToken, "input", "tap", "799", "2242");
        Console.Error.WriteLine("[tiktok] post.tap: completed; publication outcome is NOT yet confirmed");

        for (var i = 1; i <= 15; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            try
            {
                var xml = await ReadUiAsync(cancellationToken);
                var file = Path.Combine(directory, $"ui_{i:00}_after_post.xml");
                File.WriteAllText(file, xml.ToString(), new System.Text.UTF8Encoding(false));
                var markers = xml.Descendants("node")
                    .Select(n => (string?)n.Attribute("text") ?? "")
                    .Where(s => s.Length > 0)
                    .Distinct()
                    .Take(18);
                Console.Error.WriteLine($"[tiktok] post.diagnostics: sample={i:00}/15 file={file} text=[{string.Join(" | ", markers)}]");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"[tiktok] post.diagnostics: sample={i:00}/15 failed: {ex.Message}");
            }
        }
        Console.Error.WriteLine("[tiktok] post.diagnostics: finished; success NOT inferred from tap. Inspect captured XML.");
    }

    /// <summary>
    /// Prepares video and advances through gallery; never presses Post or Share.
    /// Future stages will be implemented after UI XML inspection.
    /// </summary>
    public async Task PreparePublicationAsync(
        string localPath, string caption, CancellationToken cancellationToken = default)
    {
        Console.Error.WriteLine("[tiktok] input.validate: checking source video");
        localPath = Path.GetFullPath(localPath);
        if (!File.Exists(localPath))
            throw new FileNotFoundException("Video not found.", localPath);
        if (!new[] { ".mp4", ".mov", ".m4v" }.Contains(
                Path.GetExtension(localPath), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unsupported video format.");
        if (new FileInfo(localPath).Length == 0)
            throw new InvalidOperationException("Video is empty.");
        if (string.IsNullOrWhiteSpace(caption))
            throw new ArgumentException("TikTok caption must not be empty.", nameof(caption));

        // Transfer an exact, content-addressed copy. Gallery selection below is
        // position-based and does not independently prove media identity.
        var adb = new AdbClient(options.AdbPath);
        string hash;
        await using (var stream = File.OpenRead(localPath))
            hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        var safeName = Path.GetFileName(localPath);
        var remoteName = $"tt_{hash[..16]}_{safeName}";
        var directory = options.AndroidMediaDirectory.TrimEnd('/');
        var remotePath = $"{directory}/{remoteName}";
        Console.Error.WriteLine($"[tiktok] media.push: {remoteName}");
        await adb.ShellAsync(options.DeviceId, cancellationToken, "mkdir", "-p", directory);
        await adb.PushAsync(options.DeviceId, localPath, remotePath, cancellationToken);
        var remoteHashOutput = await adb.ShellAsync(options.DeviceId, cancellationToken, "sha256sum", remotePath);
        var remoteHash = remoteHashOutput.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.Equals(hash, remoteHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"TikTok media SHA-256 mismatch: expected {hash}, got {remoteHash ?? "<empty>"}.");
        Console.Error.WriteLine("[tiktok] media.verify: SHA-256 matches");
        await adb.ShellAsync(options.DeviceId, cancellationToken, "am", "broadcast",
            "-a", "android.intent.action.MEDIA_SCANNER_SCAN_FILE", "-d", $"file://{remotePath}");
        Console.Error.WriteLine("[tiktok] media.scan: requested");

        await OpenTikTokAsync(cancellationToken);
        await Task.Delay(1500, cancellationToken);
        await OpenCreateAsync(cancellationToken);
        await Task.Delay(1500, cancellationToken);
        await OpenGalleryAsync(cancellationToken);
        await Task.Delay(700, cancellationToken);
        await SelectVideosTabAsync(cancellationToken);
        Console.Error.WriteLine("[tiktok] gallery.ready: Videos tab requested");
        await Task.Delay(900, cancellationToken);
        await SelectFirstGalleryVideoAsync(cancellationToken);
        await Task.Delay(1200, cancellationToken);
        await PressNextAsync(cancellationToken);
        await Task.Delay(1500, cancellationToken);
        await PressEditorNextAsync(cancellationToken);
        await Task.Delay(1200, cancellationToken);
        await FillCaptionAsync(caption, cancellationToken);
        await PressPostAndDiagnoseAsync(cancellationToken);
        Console.Error.WriteLine("[tiktok] publication.unconfirmed: Post tapped once; inspect diagnostics before declaring success");
    }
}
