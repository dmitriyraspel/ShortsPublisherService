namespace ShortsPublisher;

public sealed class PublisherOptions
{
    public string AdbPath { get; set; } = "adb";
    public string DeviceId { get; set; } = "emulator-5554";
    public string AndroidMediaDirectory { get; set; } = "/sdcard/Movies/ShortsPublisher";
    public string AppiumServer { get; set; } = "http://127.0.0.1:4723";
    public bool DryRun { get; set; } = true;
    public string DefaultVisibility { get; set; } = "private";
    public bool DefaultMadeForKids { get; set; } = false;
}

public sealed record JobMetadata(
    string? Title = null,
    string? Description = null,
    string? Visibility = null,
    bool? MadeForKids = null);
