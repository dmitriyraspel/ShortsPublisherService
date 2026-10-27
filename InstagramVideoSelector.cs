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

        // MediaStore may be empty on emulators. Read duration directly from the local MP4.
        var duration = ReadMp4Duration(localPath);
        var xml = instagram.SelectVideoByGalleryMetadata(duration, transferStartedLocal);
        return new OpenAttempt(hash, remotePath, null, xml);
    }

    // ISO BMFF mvhd duration (supports version 0 and 1, no external ffprobe dependency).
    private static TimeSpan ReadMp4Duration(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        static uint U32(BinaryReader r)
        {
            var bytes = r.ReadBytes(4);
            if (bytes.Length != 4) throw new EndOfStreamException();
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes);
        }
        static ulong U64(BinaryReader r)
        {
            var bytes = r.ReadBytes(8);
            if (bytes.Length != 8) throw new EndOfStreamException();
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToUInt64(bytes);
        }
        static (long Start, long End) FindBox(BinaryReader r, long from, long to, string type)
        {
            r.BaseStream.Position = from;
            while (r.BaseStream.Position + 8 <= to)
            {
                var start = r.BaseStream.Position;
                var size = U32(r);
                var tag = System.Text.Encoding.ASCII.GetString(r.ReadBytes(4));
                var header = 8L;
                long length = size;
                if (size == 1) { length = checked((long)U64(r)); header = 16; }
                else if (size == 0) length = to - start;
                if (length < header || length > to - start) break;
                if (tag == type) return (start + header, start + length);
                r.BaseStream.Position = start + length;
            }
            throw new InvalidOperationException($"MP4 box {type} not found. Nothing selected.");
        }
        var moov = FindBox(reader, 0, stream.Length, "moov");
        var mvhd = FindBox(reader, moov.Start, moov.End, "mvhd");
        stream.Position = mvhd.Start;
        var version = reader.ReadByte();
        reader.ReadBytes(3);
        if (version == 1) reader.ReadBytes(16);
        else if (version == 0) reader.ReadBytes(8);
        else throw new InvalidOperationException("Unsupported MP4 mvhd version. Nothing selected.");
        var timescale = U32(reader);
        var ticks = version == 1 ? U64(reader) : U32(reader);
        if (timescale == 0 || ticks == 0)
            throw new InvalidOperationException("Invalid MP4 duration. Nothing selected.");
        return TimeSpan.FromSeconds((double)ticks / timescale);
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
