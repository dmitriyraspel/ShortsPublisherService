using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Tasks;

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

    /// <summary>Tap the Create (+) button on TikTok's bottom navigation.</summary>
    public async Task OpenCreateAsync(CancellationToken cancellationToken = default)
    {
        Console.Error.WriteLine("[tiktok] create.open: tapping bottom-center Create button");
        var adb = new AdbClient(options.AdbPath);
        // Verified from --diagnose-ui: content-desc="Create", id=oox,
        // bounds=[432,2208][648,2337] on the 1080x2400 emulator.
        // TODO: replace fixed coordinates with a UI selector after Appium integration.
        await adb.ShellAsync(options.DeviceId, cancellationToken, "input", "tap", "540", "2272");
        Console.Error.WriteLine("[tiktok] create.open: tap completed");
    }

    /// <summary>Open the media gallery from TikTok's Create screen.</summary>
    public async Task OpenGalleryAsync(CancellationToken cancellationToken = default)
    {
        Console.Error.WriteLine("[tiktok] gallery.open: tapping bottom-left Upload button");
        var adb = new AdbClient(options.AdbPath);
        // Diagnosed UI: com.zhiliaoapp.musically:id/upload_hot_area,
        // bounds=[0,2135][179,2314] on the 1080x2400 emulator.
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
        await adb.ShellAsync(options.DeviceId, cancellationToken, "input", "tap", "516", "317");
        Console.Error.WriteLine("[tiktok] gallery.filter: tap completed; inspect --diagnose-ui");
    }

    /// <summary>
    /// Safe placeholder. Does not press Upload, Next, Post, or Share.
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

        // Transfer an exact, content-addressed copy; never select a gallery tile by
        // position or duration alone (different videos may share the same duration).
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
        Console.Error.WriteLine("[tiktok] gallery.ready: Videos tab requested; inspect --diagnose-ui");
        Console.Error.WriteLine("[tiktok] gallery.selection: skipped; XML exposes durations and thumbnails but no file identity");
        Console.Error.WriteLine("[tiktok] publication.pending: no video selected, Next/Post not pressed; nothing published");
    }
}
