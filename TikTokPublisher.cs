using System;
using System.Diagnostics;
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

        await OpenTikTokAsync(cancellationToken);
        Console.Error.WriteLine("[tiktok] publication.pending: UI flow not implemented; nothing published");
        // No publish action until the TikTok UI and confirmation signal are verified.
    }
}
