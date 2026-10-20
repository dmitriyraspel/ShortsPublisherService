using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;

namespace ShortsPublisher;

/// <summary>
/// Live UI upload intentionally fails closed until selectors are verified for the installed YouTube build.
/// </summary>
public sealed class ShortsUploadAutomation(PublisherOptions options) : IDisposable
{
    private AndroidDriver? _driver;
    private readonly PublisherOptions _options = options;

    public async Task PublishShortAsync(
        string androidPath,
        string title,
        string description,
        string visibility,
        bool madeForKids,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (_options.DryRun)
        {
            // Dry run verifies file validation and ADB transfer, but does not claim a YouTube upload.
            return;
        }

        Connect();
        _driver!.ActivateApp("com.google.android.youtube");

        throw new InvalidOperationException(
            "LIVE_UPLOAD_NOT_IMPLEMENTED: YouTube UI selectors have not yet been verified for this app version. " +
            "No publication confirmation was issued.");
    }

    private void Connect()
    {
        if (_driver is not null) return;
        var caps = new AppiumOptions();
        caps.PlatformName = "Android";
        caps.AutomationName = "UiAutomator2";
        caps.DeviceName = "Pixel 6a";
        caps.Udid = _options.DeviceId;
        caps.AddAdditionalAppiumOption("appPackage", "com.google.android.youtube");
        caps.AddAdditionalAppiumOption("noReset", true);
        caps.AddAdditionalAppiumOption("newCommandTimeout", 180);
        _driver = new AndroidDriver(new Uri(_options.AppiumServer), caps, TimeSpan.FromMinutes(3));
    }

    public void Dispose()
    {
        try { _driver?.Quit(); } catch { }
        _driver?.Dispose();
    }
}
