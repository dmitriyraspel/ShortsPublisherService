
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
var diagnoseUi = false;
var testCreate = false;
var testGallery = false;
var testSelectVideo = false;
var testNext = false;
var testDone = false;
var testEditorNext = false;
var testShowMore = false;
var testSetCaption = false;
var inspectSettings = false;
var testOpenFirstSettingsRow = false;
var testBackFromVisibility = false;
var testOpenSelectAudience = false;
var testSelectNotMadeForKids = false;
var testUploadShort = false;

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

        case "--result-file" when i + 1 < args.Length:
            resultFile = args[++i];
            break;

        case "--diagnose-ui":
            diagnoseUi = true;
            break;
        case "--test-create":
            testCreate = true;
            break;
        case "--test-gallery":
            testGallery = true;
            break;
        case "--test-select-video":
            testSelectVideo = true;
            break;
        case "--test-next":
            testNext = true;
            break;
        case "--test-done":
            testDone = true;
            break;
        case "--test-editor-next":
            testEditorNext = true;
            break;
        case "--test-show-more":
            testShowMore = true;
            break;
        case "--test-set-caption":
            testSetCaption = true;
            break;
        case "--inspect-settings":
            inspectSettings = true;
            break;
        case "--test-open-first-settings-row":
            testOpenFirstSettingsRow = true;
            break;
        case "--test-back-from-visibility":
            testBackFromVisibility = true;
            break;
        case "--test-open-select-audience":
            testOpenSelectAudience = true;
            break;
        case "--test-select-not-made-for-kids":
            testSelectNotMadeForKids = true;
            break;
        case "--test-upload-short":
            testUploadShort = true;
            break;

        case "--help":
        case "-h":
            PrintUsage();
            return ExitSuccess;

        default:
            return await EmitAsync(
                new JobResult(
                    taskId,
                    "invalid_arguments",
                    false,
                    false,
                    null,
                    $"Unknown or incomplete argument: {args[i]}",
                    null,
                    startedAt,
                    DateTimeOffset.UtcNow),
                resultFile,
                ExitBadArguments);
    }
}

var configPath = Path.Combine(
    Directory.GetCurrentDirectory(),
    "appsettings.json");

IConfigurationRoot configuration;

try
{
    configuration = new ConfigurationBuilder()
        .AddJsonFile(configPath, optional: false, reloadOnChange: false)
        .AddEnvironmentVariables("SHORTSPUBLISHER_")
        .Build();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Configuration error: {ex.Message}");
    return ExitFailure;
}

var options =
    configuration.GetSection("Publisher").Get<PublisherOptions>()
    ?? new PublisherOptions();

// Диагностический режим: подключается к Android и читает экран.
// Видео не загружает и публикацию не выполняет.
if (diagnoseUi)
{
    try
    {
        using var automation = new ShortsUploadAutomation(options);

        var pageSource = automation.ReadCurrentScreen();

        var outputPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "appium-screen.xml");

        await File.WriteAllTextAsync(outputPath, pageSource);

        Console.WriteLine($"Screen hierarchy saved to: {outputPath}");
        Console.WriteLine($"XML length: {pageSource.Length}");

        return ExitSuccess;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            $"Appium diagnostic failed: {ex}");

        return ExitFailure;
    }
}


if (testCreate)
{
    try
    {
        using var automation = new ShortsUploadAutomation(options);

        Console.WriteLine("Looking for the YouTube Create button...");
        var pageSource = automation.TestCreateButton();

        var outputPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "appium-create-menu.xml");

        await File.WriteAllTextAsync(outputPath, pageSource);

        Console.WriteLine($"Create menu hierarchy saved to: {outputPath}");
        Console.WriteLine($"XML length: {pageSource.Length}");

        return ExitSuccess;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Create button test failed: {ex}");
        return ExitFailure;
    }
}


if (testGallery)
{
    try
    {
        using var automation = new ShortsUploadAutomation(options);

        Console.WriteLine("Opening Add from Gallery...");
        var pageSource = automation.TestAddFromGallery();

        var outputPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "appium-gallery.xml");

        await File.WriteAllTextAsync(outputPath, pageSource);

        Console.WriteLine($"Gallery screen saved to: {outputPath}");
        Console.WriteLine($"XML length: {pageSource.Length}");

        return ExitSuccess;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Gallery test failed: {ex}");
        return ExitFailure;
    }
}


if (testSelectVideo)
{
    try
    {
        using var automation = new ShortsUploadAutomation(options);

        var fileName = Path.GetFileName(filePath
            ?? "video_2026-10-20_10-42-12.mp4");

        Console.WriteLine($"Selecting video: {fileName}");

        var pageSource = automation.TestSelectGalleryVideo(fileName);

        var outputPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "appium-selected-video.xml");

        await File.WriteAllTextAsync(outputPath, pageSource);

        Console.WriteLine($"Selected-video screen saved to: {outputPath}");
        Console.WriteLine($"XML length: {pageSource.Length}");

        return ExitSuccess;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Video selection test failed: {ex}");
        return ExitFailure;
    }
}

if (testNext)
{
    try
    {
        using var automation = new ShortsUploadAutomation(options);

        Console.WriteLine("Moving to the next YouTube screen...");
        var pageSource = automation.TestNextFromGallery();

        var outputPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "appium-next-screen.xml");

        await File.WriteAllTextAsync(outputPath, pageSource);

        Console.WriteLine($"Next screen saved to: {outputPath}");
        Console.WriteLine($"XML length: {pageSource.Length}");

        return ExitSuccess;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Next button test failed: {ex}");
        return ExitFailure;
    }
}


if (testDone)
{
    try
    {
        using var automation = new ShortsUploadAutomation(options);

        Console.WriteLine("Pressing Done on the Shorts editor screen...");
        var pageSource = automation.TestDoneFromEditor();

        var outputPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "appium-done-screen.xml");

        await File.WriteAllTextAsync(outputPath, pageSource);

        Console.WriteLine($"Done screen saved to: {outputPath}");
        Console.WriteLine($"XML length: {pageSource.Length}");

        return ExitSuccess;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Done button test failed: {ex}");
        return ExitFailure;
    }
}

if (testEditorNext)
{
    try
    {
        using var automation = new ShortsUploadAutomation(options);

        Console.WriteLine("Moving from Shorts editor to posting details...");
        var pageSource = automation.TestNextFromShortsEditor();

        var outputPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "appium-posting-details.xml");

        await File.WriteAllTextAsync(outputPath, pageSource);

        Console.WriteLine($"Posting details screen saved to: {outputPath}");
        Console.WriteLine($"XML length: {pageSource.Length}");

        return ExitSuccess;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Editor Next test failed: {ex}");
        return ExitFailure;
    }
}

if (testShowMore)
{
    try
    {
        using var automation = new ShortsUploadAutomation(options);
        var pageSource = automation.TestShowMoreDetails();

        var outputPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "appium-show-more.xml");

        await File.WriteAllTextAsync(outputPath, pageSource);

        Console.WriteLine($"Saved: {outputPath}");
        Console.WriteLine($"XML length: {pageSource.Length}");
        return ExitSuccess;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        return ExitFailure;
    }
}

if (testUploadShort)
{
    try
    {
        using var automation = new ShortsUploadAutomation(options);

        var pageSource = automation.TestUploadShort();
        var outputPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "appium-after-upload-short.xml");

        await File.WriteAllTextAsync(outputPath, pageSource);

        Console.WriteLine($"Screen hierarchy saved to: {outputPath}");
        Console.WriteLine($"XML length: {pageSource.Length}");

        return ExitSuccess;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Upload Short test failed: {ex}");
        return ExitFailure;
    }
}

if (testSetCaption)
{
    try
    {
        using var automation = new ShortsUploadAutomation(options);

        const string caption = "Красный сигнал ночью 🚨 #shorts";
        var pageSource = automation.TestSetCaption(caption);

        var outputPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "appium-caption.xml");

        await File.WriteAllTextAsync(outputPath, pageSource);

        Console.WriteLine($"Caption test completed. XML saved: {outputPath}");
        var captionVerified =
            pageSource.Contains("Красный сигнал ночью") &&
            pageSource.Contains("#shorts");

        Console.WriteLine($"Caption found in screen XML: {captionVerified}");

        return ExitSuccess;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Caption test failed: {ex}");
        return ExitFailure;
    }
}


if (inspectSettings)
{
    try
    {
        using var automation = new ShortsUploadAutomation(options);
        var result = automation.InspectSettingsRows();

        var outputPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "appium-settings-rows.txt");

        await File.WriteAllTextAsync(outputPath, result);

        Console.WriteLine($"Settings rows saved to: {outputPath}");
        Console.WriteLine(result);

        return ExitSuccess;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Settings inspection failed: {ex}");
        return ExitFailure;
    }
}


if (testOpenFirstSettingsRow)
{
    try
    {
        using var automation = new ShortsUploadAutomation(options);
        var pageSource = automation.TestOpenFirstSettingsRow();

        var outputPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "appium-visibility-menu.xml");

        await File.WriteAllTextAsync(outputPath, pageSource);

        Console.WriteLine($"Screen saved to: {outputPath}");
        Console.WriteLine($"XML length: {pageSource.Length}");

        Console.WriteLine(
            $"Contains Public: {pageSource.Contains("Public", StringComparison.OrdinalIgnoreCase)}");

        return ExitSuccess;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Settings-row test failed: {ex.Message}");
        return ExitFailure;
    }
}


if (testBackFromVisibility)
{
    try
    {
        using var automation = new ShortsUploadAutomation(options);
        var pageSource = automation.TestBackFromVisibility();

        var outputPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "appium-after-back.xml");

        await File.WriteAllTextAsync(outputPath, pageSource);

        Console.WriteLine($"Screen saved to: {outputPath}");
        Console.WriteLine($"XML length: {pageSource.Length}");

        return ExitSuccess;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Back navigation failed: {ex.Message}");
        return ExitFailure;
    }
}


if (testOpenSelectAudience)
{
    try
    {
        using var automation = new ShortsUploadAutomation(options);
        var pageSource = automation.TestOpenSelectAudience();

        var outputPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "appium-audience-menu.xml");

        await File.WriteAllTextAsync(outputPath, pageSource);

        Console.WriteLine($"Screen saved to: {outputPath}");
        Console.WriteLine($"XML length: {pageSource.Length}");

        return ExitSuccess;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Select audience test failed: {ex.Message}");
        return ExitFailure;
    }
}


if (testSelectNotMadeForKids)
{
    try
    {
        using var automation = new ShortsUploadAutomation(options);
        var pageSource = automation.TestSelectNotMadeForKids();

        var outputPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "appium-audience-after-selection.xml");

        await File.WriteAllTextAsync(outputPath, pageSource);

        Console.WriteLine($"Screen saved to: {outputPath}");
        Console.WriteLine($"XML length: {pageSource.Length}");

        return ExitSuccess;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Audience selection failed: {ex.Message}");
        return ExitFailure;
    }
}




// В обычном режиме файл обязателен.
if (string.IsNullOrWhiteSpace(filePath))
{
    PrintUsage();

    return await EmitAsync(
        new JobResult(
            taskId,
            "invalid_arguments",
            false,
            false,
            null,
            "Required argument --file was not provided.",
            null,
            startedAt,
            DateTimeOffset.UtcNow),
        resultFile,
        ExitBadArguments);
}

filePath = Path.GetFullPath(filePath);

try
{
    if (!File.Exists(filePath))
    {
        throw new FileNotFoundException(
            "Input video file does not exist.",
            filePath);
    }

    var extension = Path.GetExtension(filePath);

    if (!new[] { ".mp4", ".mov", ".m4v" }.Contains(
            extension,
            StringComparer.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "Unsupported file extension. Supported: .mp4, .mov, .m4v.");
    }

    var info = new FileInfo(filePath);

    if (info.Length == 0)
    {
        throw new InvalidOperationException(
            "Input video file is empty.");
    }

    var metadataPath = Path.ChangeExtension(filePath, ".json");

    var metadata = File.Exists(metadataPath)
        ? JsonSerializer.Deserialize<JobMetadata>(
            await File.ReadAllTextAsync(metadataPath))
        : null;

    var title = (
        metadata?.Title
        ?? Path.GetFileNameWithoutExtension(filePath)).Trim();

    if (string.IsNullOrWhiteSpace(title))
    {
        throw new InvalidOperationException("Video title is empty.");
    }

    if (title.Length > 100)
    {
        title = title[..100];
    }

    var visibility = (
        metadata?.Visibility
        ?? options.DefaultVisibility).ToLowerInvariant();

    if (visibility is not ("private" or "unlisted" or "public"))
    {
        throw new InvalidOperationException(
            "Visibility must be private, unlisted, or public.");
    }

    await using var stream = File.OpenRead(filePath);

    var sha256 = Convert.ToHexString(
        await SHA256.HashDataAsync(stream)).ToLowerInvariant();

    var adb = new AdbClient(options.AdbPath);

    var devices = await adb.DevicesAsync(CancellationToken.None);

    if (!devices.Split('\n').Any(line =>
            line.Trim().StartsWith(
                options.DeviceId + "\tdevice",
                StringComparison.Ordinal)))
    {
        throw new InvalidOperationException(
            $"Android device '{options.DeviceId}' is not ready. " +
            $"ADB devices output: {devices}");
    }

    var remotePath =
        $"{options.AndroidMediaDirectory.TrimEnd('/')}/{Path.GetFileName(filePath)}";

    await adb.ShellAsync(
        options.DeviceId,
        CancellationToken.None,
        "mkdir",
        "-p",
        options.AndroidMediaDirectory);

    await adb.PushAsync(
        options.DeviceId,
        filePath,
        remotePath,
        CancellationToken.None);

    await adb.ShellAsync(
        options.DeviceId,
        CancellationToken.None,
        "am",
        "broadcast",
        "-a",
        "android.intent.action.MEDIA_SCANNER_SCAN_FILE",
        "-d",
        $"file://{remotePath}");

    using var automation = new ShortsUploadAutomation(options);

    await automation.PublishShortAsync(
        remotePath,
        title,
        metadata?.Description ?? "",
        visibility,
        metadata?.MadeForKids ?? options.DefaultMadeForKids,
        CancellationToken.None);

    var status = options.DryRun
        ? "dry_run_completed"
        : "published";

    var message = options.DryRun
        ? "Validation and ADB transfer succeeded. Dry run only: YouTube publication was not attempted."
        : "Publication completed and verified.";

    var result = new JobResult(
        taskId,
        status,
        true,
        !options.DryRun,
        filePath,
        message,
        sha256,
        startedAt,
        DateTimeOffset.UtcNow);

    return await EmitAsync(result, resultFile, ExitSuccess);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);

    var result = new JobResult(
        taskId,
        "failed",
        false,
        false,
        filePath,
        ex.Message,
        null,
        startedAt,
        DateTimeOffset.UtcNow);

    return await EmitAsync(result, resultFile, ExitFailure);
}

static async Task<int> EmitAsync(
    JobResult result,
    string? resultFile,
    int exitCode)
{
    var json = JsonSerializer.Serialize(
        result,
        new JsonSerializerOptions { WriteIndented = true });

    Console.Out.WriteLine(json);

    if (!string.IsNullOrWhiteSpace(resultFile))
    {
        var fullPath = Path.GetFullPath(resultFile);

        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

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
      ShortsPublisher.exe --diagnose-ui
      ShortsPublisher.exe --test-create

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
