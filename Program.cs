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
string? caption = null;
string? resultFile = null;

// Kept for backwards compatibility with the verified invocation. Publishing is
// now the normal processing path, whether or not this switch is supplied.
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--task-id" when i + 1 < args.Length:
            taskId = args[++i];
            break;
        case "--file" when i + 1 < args.Length:
            filePath = args[++i];
            break;
        case "--caption" when i + 1 < args.Length:
            caption = args[++i];
            break;
        case "--result-file" when i + 1 < args.Length:
            resultFile = args[++i];
            break;
        case "--publish-video-youtube":
            break;
        case "--help":
        case "-h":
            PrintUsage();
            return ExitSuccess;
        default:
            return await EmitAsync(
                Failure(taskId, "invalid_arguments", $"Unknown or incomplete argument: {args[i]}", startedAt),
                resultFile,
                ExitBadArguments);
    }
}

if (string.IsNullOrWhiteSpace(filePath))
{
    return await EmitAsync(
        Failure(taskId, "invalid_arguments", "Required argument --file was not provided.", startedAt),
        resultFile,
        ExitBadArguments);
}

filePath = Path.GetFullPath(filePath);

try
{
    var configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
    var configuration = new ConfigurationBuilder()
        .AddJsonFile(configPath, optional: false, reloadOnChange: false)
        .AddEnvironmentVariables("SHORTSPUBLISHER_")
        .Build();
    var options = configuration.GetSection("Publisher").Get<PublisherOptions>() ?? new PublisherOptions();

    if (!File.Exists(filePath))
        throw new FileNotFoundException("Input video file does not exist.", filePath);

    var extension = Path.GetExtension(filePath);
    if (!new[] { ".mp4", ".mov", ".m4v" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
        throw new InvalidOperationException("Unsupported file extension. Supported: .mp4, .mov, .m4v.");

    var info = new FileInfo(filePath);
    if (info.Length == 0)
        throw new InvalidOperationException("Input video file is empty.");

    var metadataPath = Path.ChangeExtension(filePath, ".json");
    JobMetadata? metadata = null;
    if (File.Exists(metadataPath))
    {
        metadata = JsonSerializer.Deserialize<JobMetadata>(
            await File.ReadAllTextAsync(metadataPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    var effectiveCaption = !string.IsNullOrWhiteSpace(caption)
        ? caption.Trim()
        : !string.IsNullOrWhiteSpace(metadata?.Caption)
            ? metadata.Caption.Trim()
            : !string.IsNullOrWhiteSpace(metadata?.Title)
                ? metadata.Title.Trim()
                : Path.GetFileNameWithoutExtension(filePath);

    if (string.IsNullOrWhiteSpace(effectiveCaption))
        throw new InvalidOperationException("Caption is empty.");
    if (effectiveCaption.Length > 100)
        effectiveCaption = effectiveCaption[..100];

    await using var stream = File.OpenRead(filePath);
    var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();

    using var cancellation = new CancellationTokenSource();
    var adb = new AdbClient(options.AdbPath);
    var devices = await adb.DevicesAsync(cancellation.Token);
    if (!devices.Split('\n').Any(line =>
            line.Trim().StartsWith(options.DeviceId + "\tdevice", StringComparison.Ordinal)))
    {
        throw new InvalidOperationException($"Android device '{options.DeviceId}' is not ready.");
    }

    var remotePath = $"{options.AndroidMediaDirectory.TrimEnd('/')}/{Path.GetFileName(filePath)}";
    await adb.ShellAsync(options.DeviceId, cancellation.Token, "mkdir", "-p", options.AndroidMediaDirectory);
    await adb.PushAsync(options.DeviceId, filePath, remotePath, cancellation.Token);
    await adb.ShellAsync(
        options.DeviceId,
        cancellation.Token,
        "am", "broadcast",
        "-a", "android.intent.action.MEDIA_SCANNER_SCAN_FILE",
        "-d", $"file://{remotePath}");

    using var automation = new ShortsUploadAutomation(options);
    var observation = automation.PublishVideoOnYouTubeAsync(Path.GetFileName(filePath), effectiveCaption);

    var result = new JobResult(
        taskId,
        observation.Status,
        observation.Published,
        observation.Published,
        filePath,
        observation.Message,
        sha256,
        startedAt,
        DateTimeOffset.UtcNow);

    return await EmitAsync(result, resultFile, observation.Published ? ExitSuccess : ExitFailure);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return await EmitAsync(
        Failure(taskId, "failed", ex.Message, startedAt, filePath),
        resultFile,
        ExitFailure);
}

static JobResult Failure(
    string? taskId,
    string status,
    string message,
    DateTimeOffset startedAt,
    string? file = null) =>
    new(taskId, status, false, false, file, message, null, startedAt, DateTimeOffset.UtcNow);

static async Task<int> EmitAsync(JobResult result, string? resultFile, int exitCode)
{
    var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    if (!string.IsNullOrWhiteSpace(resultFile))
    {
        try
        {
            var fullPath = Path.GetFullPath(resultFile);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var tempPath = fullPath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch (Exception ex)
        {
            // Keep stdout machine-readable and emit exactly one result object.
            Console.Error.WriteLine($"Could not write --result-file: {ex.Message}");
        }
    }

    Console.Out.WriteLine(json);
    return exitCode;
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
    ShortsPublisherService — publishes one video per invocation.

    Usage:
      ShortsPublisher.exe --file "C:\Videos\clip.mp4" --caption "Caption" [--task-id task-123] [--result-file "C:\Results\task-123.json"]

    Arguments:
      --file PATH          Required input video (.mp4, .mov, .m4v).
      --caption TEXT       Optional caption; falls back to sidecar Caption, Title, then filename.
      --task-id ID         Optional task identifier.
      --result-file PATH   Optional path to save the JSON result.
      --publish-video-youtube  Backwards-compatible switch; publishing is the default.
      --help, -h           Show this help.

    Exit codes:
      0 = publication confirmed.
      1 = processing failed or publication unconfirmed.
      2 = invalid arguments.
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
