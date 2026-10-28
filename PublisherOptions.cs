namespace ShortsPublisher;

public sealed class PublisherOptions
{
    public string AdbPath { get; set; } = "adb";
    public string DeviceId { get; set; } = "emulator-5554";
    public string AndroidMediaDirectory { get; set; } = "/sdcard/Movies/ShortsPublisher";
    public string AppiumServer { get; set; } = "http://127.0.0.1:4723";
}

// Caption is the text entered in the verified YouTube Shorts publishing flow.
// Title is retained as a backwards-compatible fallback for existing sidecar files.
public sealed record JobMetadata(string? Caption = null, string? Title = null);
