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

        // Facebook can reopen on Reels or a profile. Navigate to Home first:
        // the Home header's Create button is more reliable than the Reels header.
        var home = _driver.FindElements(By.XPath(
            "//*[@clickable='true' and (starts-with(@content-desc,'Главная, вкладка') or starts-with(@content-desc,'Home, tab'))]"))
            .FirstOrDefault(element => element.Displayed && element.Enabled);
        if (home is not null)
        {
            home.Click();
            Console.Error.WriteLine("[facebook-reel] navigation.home: tapped Home tab");
        }

        var createDeadline = DateTime.UtcNow.AddSeconds(15);
        AppiumElement? create = null;
        while (DateTime.UtcNow < createDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            create = _driver.FindElements(By.XPath(
                "//*[@clickable='true' and (starts-with(@content-desc,'Создать:') or starts-with(@content-desc,'Create,'))]"))
                .FirstOrDefault(element => element.Displayed && element.Enabled);
            if (create is not null) break;
            Thread.Sleep(400);
        }
        if (create is null)
            return Failure("facebook_reel_create_button_not_found",
                "Home Create button was not found. No publication attempted.");

        create.Click();
        Console.Error.WriteLine("[facebook-reel] create.menu: tapped Home Create; looking for Reel option");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reel = _driver.FindElements(By.XPath(
                "//*[@clickable='true' and (@content-desc='Видео Reels' or @content-desc='Reel' or @content-desc='Создать видео Reels' or @content-desc='Create reel' or @text='Видео Reels' or @text='Reel' or @text='Создать видео Reels' or @text='Create reel')]"))
                .FirstOrDefault(element => element.Displayed && element.Enabled);
            if (reel is not null)
            {
                reel.Click();
                Console.Error.WriteLine("[facebook-reel] create.open: tapped Reel option");
                var galleryDeadline = DateTime.UtcNow.AddSeconds(15);
                while (DateTime.UtcNow < galleryDeadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_driver.FindElements(By.XPath(
                        "//*[@text='Создание видео Reels' or @content-desc='Создание видео Reels' or @text='Create reel' or @content-desc='Create reel']"))
                        .Any(element => element.Displayed))
                        return new FacebookReelsPreparationResult(
                            "facebook_reel_creation_opened",
                            "Facebook Reels creation gallery opened from Home. No video selected or published.");
                    Thread.Sleep(400);
                }
                return Failure("facebook_reel_gallery_not_found",
                    "Reel option tapped, but creation gallery not confirmed. No publication attempted.");
            }
            Thread.Sleep(400);
        }

        return Failure("facebook_reel_option_not_found",
            "Home Create menu opened, but Reel option was not found. No publication attempted.");
    }

    private FacebookReelsPreparationResult Failure(string status, string message)
    {
        string? diagnosticPath = null;
        try
        {
            var directory = Path.Combine("diagnostics", "facebook-reels");
            Directory.CreateDirectory(directory);
            diagnosticPath = Path.Combine(directory,
                $"navigation_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}.xml");
            File.WriteAllText(diagnosticPath, _driver!.PageSource);
            Console.Error.WriteLine($"[facebook-reel] diagnostics.xml: {diagnosticPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[facebook-reel] diagnostics.xml warning: {ex.Message}");
        }
        return new FacebookReelsPreparationResult(status,
            $"{message} Diagnostic XML: {diagnosticPath ?? "<unavailable>"}");
    }

    public void Dispose()
    {
        try { _driver?.Quit(); } catch { }
        _driver?.Dispose();
    }
}

public sealed record FacebookReelsPreparationResult(string Status, string Message);
