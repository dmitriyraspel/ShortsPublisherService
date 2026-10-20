
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;

namespace ShortsPublisher;

/// <summary>
/// YouTube UI automation. Live publication remains disabled until verified.
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
            // Dry run does not open YouTube or attempt publication.
            return;
        }

        Connect();
        _driver!.ActivateApp("com.google.android.youtube");

        throw new InvalidOperationException(
            "LIVE_UPLOAD_NOT_IMPLEMENTED: UI selectors have not been verified. " +
            "No publication confirmation was issued.");
    }

    public string ReadCurrentScreen()
    {
        Connect();
        return _driver!.PageSource;
    }

    public string TestCreateButton()
    {
        Connect();

        var createButton = _driver!.FindElement(
            By.XPath(
                "//*[@content-desc='Create' and @clickable='true']"));

        createButton.Click();
        Thread.Sleep(1500);

        return _driver.PageSource;
    }

    public string TestAddFromGallery()
    {
        Connect();

        var galleryButton = _driver!.FindElement(
            By.Id("com.google.android.youtube:id/unified_permissions_primary_button"));

        if (galleryButton.Text != "Add from Gallery")
        {
            throw new InvalidOperationException(
                $"Unexpected button text: '{galleryButton.Text}'.");
        }

        galleryButton.Click();
        Thread.Sleep(1500);

        return _driver.PageSource;
    }



    public string TestSelectGalleryVideo(string fileName)
    {
        Connect();

        var video = _driver!.FindElement(
            By.XPath("//*[@content-desc=" + XPathLiteral(fileName) + "]"));

        var clickableContainer = _driver.FindElement(
            By.XPath(
                "//*[@content-desc=" + XPathLiteral(fileName) +
                "]/ancestor::android.widget.FrameLayout[@clickable='true']"));

        clickableContainer.Click();

        Thread.Sleep(1000);

        return _driver.PageSource;
    }

    public string TestNextFromGallery()
    {
        Connect();

        var nextButton = _driver!.FindElement(
            By.Id("com.google.android.youtube:id/multi_select_next_button"));

        if (!nextButton.Enabled)
        {
            throw new InvalidOperationException(
                "The Next button is disabled.");
        }

        nextButton.Click();
        Thread.Sleep(2000);

        return _driver.PageSource;
    }

    public string TestDoneFromEditor()
    {
        Connect();

        var doneButton = _driver!.FindElement(
            By.Id("com.google.android.youtube:id/creation_next_button"));

        if (!doneButton.Enabled)
        {
            throw new InvalidOperationException(
                "The Done button is disabled.");
        }

        doneButton.Click();
        Thread.Sleep(2000);

        return _driver.PageSource;
    }

    public string TestNextFromShortsEditor()
    {
        Connect();

        var nextButton = _driver!.FindElement(
            By.Id("com.google.android.youtube:id/shorts_post_bottom_button"));

        if (!nextButton.Enabled)
        {
            throw new InvalidOperationException(
                "The Next button is disabled.");
        }

        nextButton.Click();
        Thread.Sleep(2000);

        return _driver.PageSource;
    }

    public string TestShowMoreDetails()
    {
        Connect();

        var showMore = _driver!.FindElement(
            By.XPath("//*[@content-desc='Show more']"));

        showMore.Click();
        Thread.Sleep(1000);

        return _driver.PageSource;
    }


    public string InspectSettingsRows()
    {
        Connect();

        var rows = _driver!.FindElements(
            By.XPath("//*[@clickable='true' and @bounds]"));

        var result = new System.Text.StringBuilder();

        foreach (var row in rows)
        {
            var bounds = row.GetAttribute("bounds");
            var text = row.Text;
            var description = row.GetAttribute("content-desc");

            result.AppendLine(
                $"Bounds={bounds} | Text={text} | Description={description}");
        }

        return result.ToString();
    }


    public string TestOpenFirstSettingsRow()
    {
        Connect();

        // Повторно проверяем, что строка из текущего XML существует.
        var rows = _driver!.FindElements(
            By.XPath("//*[@class='android.view.ViewGroup' and " +
                    "@clickable='true' and " +
                    "@bounds='[0,884][1080,1031]']"));

        if (rows.Count != 1)
        {
            throw new InvalidOperationException(
                "Expected settings row was not found uniquely. Nothing was clicked.");
        }

        rows[0].Click();
        Thread.Sleep(700);

        return _driver.PageSource;
    }


    public string TestBackFromVisibility()
    {
        Connect();

        var backButton = _driver!.FindElement(
            By.XPath("//*[@content-desc='Back' and @clickable='true']"));

        backButton.Click();
        Thread.Sleep(700);

        return _driver.PageSource;
    }



    public string TestSetCaption(string caption)
    {
        Connect();

        var captionField = _driver!.FindElement(
            By.XPath("//android.widget.EditText[@hint='Caption your Short']"));

        captionField.Click();
        captionField.Clear();
        captionField.SendKeys(caption);

        // Скрываем клавиатуру, чтобы сохранить XML всего экрана.
        try
        {
            _driver.HideKeyboard();
        }
        catch
        {
            // Клавиатура могла уже скрыться.
        }

        Thread.Sleep(500);

        return _driver.PageSource;
    }


    public string TestOpenSelectAudience()
    {
        Connect();

        // Ищем второй пункт в списке настроек по его позиции.
        var rows = _driver!.FindElements(
            By.XPath("//*[@clickable='true' and @bounds]"));

        var target = rows.FirstOrDefault(
            row => row.GetAttribute("bounds") == "[0,1031][1080,1178]");

        if (target == null)
        {
            throw new InvalidOperationException(
                "Select audience row not found at the expected bounds. Nothing was clicked.");
        }

        target.Click();
        Thread.Sleep(700);

        return _driver.PageSource;
    }


    public string TestSelectNotMadeForKids()
    {
        Connect();

        var target = _driver!.FindElements(
            By.XPath("//*[@clickable='true' and @bounds]"))
            .FirstOrDefault(row =>
                row.GetAttribute("bounds") == "[0,813][1080,960]");

        if (target == null)
        {
            throw new InvalidOperationException(
                "Audience option not found at expected bounds. Nothing was clicked.");
        }

        target.Click();
        Thread.Sleep(700);

        return _driver.PageSource;
    }

    public string TestUploadShort()
    {
        Connect();

        var uploadButton = _driver!.FindElement(
            By.Id("com.google.android.youtube:id/upload_bottom_button"));

        if (!uploadButton.Displayed || !uploadButton.Enabled)
            throw new InvalidOperationException(
                "Upload Short button is not visible or enabled.");

        uploadButton.Click();
        Thread.Sleep(2000);

        return _driver.PageSource;
    }



    private static string XPathLiteral(string value)
    {
        if (!value.Contains('\''))
            return "'" + value + "'";

        if (!value.Contains('"'))
            return "\"" + value + "\"";

        var parts = value.Split('\'');
        return "concat('" + string.Join("', \"'\", '", parts) + "')";
    }



    private void Connect()
    {
        if (_driver is not null) return;

        var caps = new AppiumOptions();
        caps.PlatformName = "Android";
        caps.AutomationName = "UiAutomator2";
        caps.DeviceName = "Pixel 6a";
        caps.AddAdditionalAppiumOption("udid", _options.DeviceId);
        caps.AddAdditionalAppiumOption("appPackage", "com.google.android.youtube");
        caps.AddAdditionalAppiumOption("noReset", true);
        caps.AddAdditionalAppiumOption("newCommandTimeout", 180);

        _driver = new AndroidDriver(
            new Uri(_options.AppiumServer),
            caps,
            TimeSpan.FromMinutes(3));
    }

    public void Dispose()
    {
        try { _driver?.Quit(); } catch { }
        _driver?.Dispose();
    }
}
