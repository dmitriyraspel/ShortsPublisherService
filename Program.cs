using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ShortsPublisher;

const int ExitSuccess = 0;
const int ExitFailure = 1;
const int ExitBadArguments = 2;

var startedAt = DateTimeOffset.UtcNow;
string? taskId = null;
string? filePath = null;
string? resultFile = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--task-id" when i + 1 < args.Length: taskId = args[++i]; break;
        case "--file" when i + 1 < args.Length: filePath = args[++i]; break;
        case "--result-file" when i + 1 < args.Length: resultFile = args[++i]; break;
        case "--help":
        case "-h":
            PrintUsage();
            return ExitSuccess;
        default:
            return await EmitAsync(new JobResult(taskId, "invalid_arguments", false, false, null,
                $"Unknown or incomplete argument: {args[i]}", null, startedAt, DateTimeOffset.UtcNow),
                resultFile, ExitBadArguments);
    }
}

if (string.IsNullOrWhiteSpace(filePath))
{
    PrintUsage();
    return await EmitAsync(new JobResult(taskId, "invalid_arguments", false, false, null,
        "Required argument --file was not provided.", null, startedAt, DateTimeOffset.UtcNow),
        resultFile, ExitBadArguments);
}

filePath = Path.GetFullPath(filePath);
var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
var configuration = new ConfigurationBuilder()
    .AddJsonFile(configPath, optional: false, reloadOnChange: false)
    .AddEnvironmentVariables("SHORTSPUBLISHER_")
    .Build();
var options = configuration.GetSection("Publisher").Get<PublisherOptions>() ?? new PublisherOptions();

try
{
    if (!File.Exists(filePath))
        throw new FileNotFoundException("Input video file does not exist.", filePath);

    var extension = Path.GetExtension(filePath);
    if (!new[] { ".mp4", ".mov", ".m4v" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
        throw new InvalidOperationException("Unsupported file extension. Supported: .mp4, .mov, .m4v.");

    var info = new FileInfo(filePath);
    if (info.Length == 0)
        throw new InvalidOperationException("Input video file is empty.");

    var metadataPath = Path.ChangeExtension(filePath, ".json");
    var metadata = File.Exists(metadataPath)
        ? JsonSerializer.Deserialize<JobMetadata>(await File.ReadAllTextAsync(metadataPath))
        : null;

    var title = (metadata?.Title ?? Path.GetFileNameWithoutExtension(filePath)).Trim();
    if (string.IsNullOrWhiteSpace(title))
        throw new InvalidOperationException("Video title is empty.");
    if (title.Length > 100) title = title[..100];

    var visibility = (metadata?.Visibility ?? options.DefaultVisibility).ToLowerInvariant();
    if (visibility is not ("private" or "unlisted" or "public"))
        throw new InvalidOperationException("Visibility must be private, unlisted, or public.");

    await using var stream = File.OpenRead(filePath);
    var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();

    var adb = new AdbClient(options.AdbPath);
    var devices = await adb.DevicesAsync(CancellationToken.None);
    if (!devices.Split('\n').Any(line =>
            line.Trim().StartsWith(options.DeviceId + "\tdevice", StringComparison.Ordinal)))
        throw new InvalidOperationException(
            $"Android device '{options.DeviceId}' is not ready. ADB devices output: {devices}");

    var remotePath = $"{options.AndroidMediaDirectory.TrimEnd('/')}/{Path.GetFileName(filePath)}";
    await adb.ShellAsync(options.DeviceId, CancellationToken.None, "mkdir", "-p", options.AndroidMediaDirectory);
    await adb.PushAsync(options.DeviceId, filePath, remotePath, CancellationToken.None);
    await adb.ShellAsync(options.DeviceId, CancellationToken.None,
        "am", "broadcast", "-a", "android.intent.action.MEDIA_SCANNER_SCAN_FILE", "-d", $"file://{remotePath}");

    using var automation = new ShortsUploadAutomation(options);
    await automation.PublishShortAsync(remotePath, title, metadata?.Description ?? "",
        visibility, metadata?.MadeForKids ?? options.DefaultMadeForKids, CancellationToken.None);

    var status = options.DryRun ? "dry_run_completed" : "published";
    var message = options.DryRun
        ? "Validation and ADB transfer succeeded. Dry run only: YouTube publication was not attempted."
        : "Publication completed and verified.";
    var result = new JobResult(taskId, status, true, !options.DryRun, filePath, message,
        sha256, startedAt, DateTimeOffset.UtcNow);

    return await EmitAsync(result, resultFile, ExitSuccess);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    var result = new JobResult(taskId, "failed", false, false, filePath,
        ex.Message, null, startedAt, DateTimeOffset.UtcNow);
    return await EmitAsync(result, resultFile, ExitFailure);
}

static async Task<int> EmitAsync(JobResult result, string? resultFile, int exitCode)
{
    var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    Console.Out.WriteLine(json);

    if (!string.IsNullOrWhiteSpace(resultFile))
    {
        var fullPath = Path.GetFullPath(resultFile);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var tempPath = fullPath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, fullPath, overwrite: true);
    }

    return exitCode;
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
    ShortsPublisher — one invocation processes one video.

    Usage:
      ShortsPublisher.exe --file "C:\videos\clip.mp4" [--task-id 123] [--result-file "C:\results\123.json"]

    Exit codes:
      0 = task handled successfully (check status; dry_run_completed is NOT published)
      1 = processing failed; external orchestrator may retry
      2 = invalid arguments
    """);
}

public sealed record JobResult(
    string? TaskId,
    string Status,
    bool Success,
    bool Published,
    string? File,
    string Message,
    string? Sha256,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);
