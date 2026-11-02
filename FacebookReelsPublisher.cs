using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;

namespace ShortsPublisher;

/// <summary>
/// First stage of Facebook Reels automation: open Facebook and locate the Reel creation entry.
/// Never taps Share/Publish. The video upload and publishing flow will be added separately.
/// </summary>
public sealed class FacebookReelsPublisher(PublisherOptions options) : IDisposable
{
    private const string FacebookPackage = "com.facebook.katana";
    private AndroidDriver? _driver;

    public FacebookReelsPreparationResult OpenReelCreation(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.Error.WriteLine("[facebook-reel] app.open: connecting to Facebook");
        var caps = new AppiumOptions();
        caps.PlatformName = "Android";
        caps.AutomationName = "UiAutomator2";
        caps.DeviceName = "Pixel 6a";
        caps.AddAdditionalAppiumOption("udid", options.DeviceId);
        caps.AddAdditionalAppiumOption("appPackage", FacebookPackage);
        caps.AddAdditionalAppiumOption("noReset", true);
        caps.AddAdditionalAppiumOption("newCommandTimeout", 180);
        _driver = new AndroidDriver(new Uri(options.AppiumServer), caps, TimeSpan.FromMinutes(3));
        _driver.ActivateApp(FacebookPackage);
        Console.Error.WriteLine("[facebook-reel] app.open: ready");

        // Only use an explicitly labelled entry point. Never guess screen coordinates
        // or click a generic Share button in this exploratory stage.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = _driver.FindElements(By.XPath(
                "//*[@clickable='true' and (@content-desc='Create reel' or @content-desc='Создать видео Reels' or @content-desc='Создать Reel' or @text='Create reel' or @text='Создать видео Reels' or @text='Создать Reel')]"))
                .FirstOrDefault(element => element.Displayed && element.Enabled);
            if (entry is not null)
            {
                entry.Click();
                Console.Error.WriteLine("[facebook-reel] create.open: tapped labelled Reel creation entry");
                return new FacebookReelsPreparationResult(
                    "facebook_reel_creation_opened",
                    "Tapped the labelled Facebook Reel creation entry. No video selected or published.");
            }
            Thread.Sleep(500);
        }

        string? diagnosticPath = null;
        try
        {
            var directory = Path.Combine("diagnostics", "facebook-reels");
            Directory.CreateDirectory(directory);
            diagnosticPath = Path.Combine(directory, $"entry_not_found_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}.xml");
            File.WriteAllText(diagnosticPath, _driver.PageSource);
            Console.Error.WriteLine($"[facebook-reel] diagnostics.xml: {diagnosticPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[facebook-reel] diagnostics.xml warning: {ex.Message}");
        }

        return new FacebookReelsPreparationResult(
            "facebook_reel_entry_not_found",
            $"No labelled Reel creation entry found. No publication attempted. Diagnostic XML: {diagnosticPath ?? "<unavailable>"}");
    }

    public void Dispose()
    {
        try { _driver?.Quit(); } catch { }
        _driver?.Dispose();
    }
}

public sealed record FacebookReelsPreparationResult(string Status, string Message);
