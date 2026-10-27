using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ShortsPublisher;

/// <summary>
/// Experimental, non-publishing Instagram handoff. A verified Android copy does not
/// imply that Instagram actually selected the video.
/// </summary>
public sealed class InstagramVideoSelector(PublisherOptions options)
{
    public sealed record OpenAttempt(string Sha256, string AndroidPath, string? MediaStoreUri, string DiagnosticXmlPath);

    /// <summary>Prepare the exact input video on Android and stop at the Instagram Reel gallery.</summary>
    public async Task<OpenAttempt> PrepareGalleryAsync(string localPath, CancellationToken cancellationToken)
    {
        localPath = Path.GetFullPath(localPath);
        if (!File.Exists(localPath)) throw new FileNotFoundException("Video not found.", localPath);
        if (!new[] { ".mp4", ".mov", ".m4v" }.Contains(Path.GetExtension(localPath), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unsupported video format.");
        if (new FileInfo(localPath).Length == 0) throw new InvalidOperationException("Video is empty.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        var adb = new AdbClient(options.AdbPath);
        var devices = await adb.DevicesAsync(ct);
        if (!devices.Split('\n').Any(line => line.Trim().StartsWith(options.DeviceId + "\tdevice", StringComparison.Ordinal)))
            throw new InvalidOperationException($"Android device '{options.DeviceId}' is not ready.");

        string hash;
        await using (var stream = File.OpenRead(localPath))
            hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();

        // Preserve the supplied basename for gallery inspection; add hash to avoid stale duplicates.
        var name = $"sp_{hash[..16]}_{Path.GetFileName(localPath)}";
        var remotePath = $"{options.AndroidMediaDirectory.TrimEnd('/')}/{name}";
        await adb.ShellAsync(options.DeviceId, ct, "mkdir", "-p", options.AndroidMediaDirectory);
        await adb.PushAsync(options.DeviceId, localPath, remotePath, ct);
        var remoteHashOutput = await adb.ShellAsync(options.DeviceId, ct, "sha256sum", remotePath);
        var remoteHash = remoteHashOutput.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.Equals(hash, remoteHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Android SHA-256 mismatch: expected {hash}, got {remoteHash ?? "<empty>"}.");

        // MediaStore query is intentionally NOT a prerequisite: some Android builds
        // return no rows even after a scan. Ask MediaScanner to index the exact path.
        var deviceClock = (await adb.ShellAsync(options.DeviceId, ct, "date", "+%Y-%m-%dT%H:%M:%S")).Trim();
        if (!DateTime.TryParseExact(deviceClock, "yyyy-MM-ddTHH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var transferStartedLocal))
            throw new InvalidOperationException($"Cannot read Android device local time: {deviceClock}");
        await adb.ShellAsync(options.DeviceId, ct, "am", "broadcast",
            "-a", "android.intent.action.MEDIA_SCANNER_SCAN_FILE", "-d", $"file://{remotePath}");
        await Task.Delay(1800, ct);

        using var instagram = new InstagramPublisher(options);
        instagram.OpenInstagram();
        instagram.instagramHome();
        instagram.instagramCreate();
        instagram.instagramSelectReel();
        instagram.WaitForGallery();

        // MediaStore duration belongs to the verified content-addressed filename.
        // If indexing is incomplete, stop rather than tapping an arbitrary thumbnail.
        string rows = "";
        for (var attempt = 0; attempt < 8; attempt++)
        {
            rows = await adb.ShellAsync(options.DeviceId, ct, "content", "query",
                "--uri", "content://media/external/video/media",
                "--projection", "_id:display_name:duration",
                "--where", $"_display_name='{name}'");
            if (Regex.IsMatch(rows, @"(?:^|[,\\s])duration=\\d+")) break;
            await Task.Delay(750, ct);
        }
        var durations = Regex.Matches(rows, @"(?:^|[,\\s])duration=(\\d+)")
            .Select(m => long.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        var ids = Regex.Matches(rows, @"(?:^|[,\\s])_id=(\\d+)").Count;
        if (ids != 1 || durations.Length != 1)
            throw new InvalidOperationException(
                $"Could not uniquely identify verified video in Android MediaStore; nothing selected. Response: {rows}");
        var xml = instagram.SelectVideoByGalleryMetadata(
            TimeSpan.FromMilliseconds(durations[0]), transferStartedLocal);
        return new OpenAttempt(hash, remotePath, null, xml);
    }

    public async Task<OpenAttempt> OpenVideoAsync(string localPath, CancellationToken cancellationToken)
    {
        localPath = Path.GetFullPath(localPath);
        if (!File.Exists(localPath)) throw new FileNotFoundException("Video not found.", localPath);
        if (!new[] { ".mp4", ".mov", ".m4v" }.Contains(Path.GetExtension(localPath), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .mp4, .mov and .m4v are supported.");
        if (new FileInfo(localPath).Length == 0) throw new InvalidOperationException("Video is empty.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        var adb = new AdbClient(options.AdbPath);
        var devices = await adb.DevicesAsync(ct);
        if (!devices.Split('\n').Any(line => line.Trim().StartsWith(options.DeviceId + "\tdevice", StringComparison.Ordinal)))
            throw new InvalidOperationException($"Android device '{options.DeviceId}' is not ready.");

        string hash;
        await using (var stream = File.OpenRead(localPath))
            hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();

        // A content-addressed filename prevents confusing an older file with the new upload.
        var extension = Path.GetExtension(localPath).ToLowerInvariant();
        var name = $"sp_{hash}{extension}";
        var remotePath = $"{options.AndroidMediaDirectory.TrimEnd('/')}/{name}";
        await adb.ShellAsync(options.DeviceId, ct, "mkdir", "-p", options.AndroidMediaDirectory);
        await adb.PushAsync(options.DeviceId, localPath, remotePath, ct);

        var remoteHashOutput = await adb.ShellAsync(options.DeviceId, ct, "sha256sum", remotePath);
        var remoteHash = remoteHashOutput.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!string.Equals(hash, remoteHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Android SHA-256 mismatch: expected {hash}, got {remoteHash ?? "<empty>"}.");

        await adb.ShellAsync(options.DeviceId, ct, "am", "broadcast",
            "-a", "android.intent.action.MEDIA_SCANNER_SCAN_FILE", "-d", $"file://{remotePath}");

        // Query by unique content-addressed display name. A matching row is not proof
        // that Instagram will accept the URI, so the outcome remains experimental.
        var rows = await adb.ShellAsync(options.DeviceId, ct, "content", "query",
            "--uri", "content://media/external/video/media",
            "--projection", "_id:display_name:_size:duration",
            "--where", $"_display_name='{name}'");
        var ids = Regex.Matches(rows, @"(?:^|[,\s])_id=(\d+)", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).Distinct().ToArray();
        if (ids.Length != 1)
            throw new InvalidOperationException($"Expected one MediaStore row for {name}; found {ids.Length}. MediaStore response: {rows}");
        var uri = $"content://media/external/video/media/{ids[0]}";

        // ACTION_SEND is intentionally used without publishing. The receiving app
        // may reject the URI or open a chooser; inspect diagnostics before proceeding.
        var intentOutput = await adb.ShellAsync(options.DeviceId, ct, "am", "start",
            "-a", "android.intent.action.SEND",
            "-t", "video/mp4",
            "-p", "com.instagram.android",
            "--eu", "android.intent.extra.STREAM", uri,
            "--grant-read-uri-permission");
        if (intentOutput.Contains("Error:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Instagram intent rejected: " + intentOutput);

        await Task.Delay(1500, ct);
        var xml = new AndroidDiagnostics(options).SaveCurrentScreenXml();
        return new OpenAttempt(hash, remotePath, uri, xml);
    }
}
