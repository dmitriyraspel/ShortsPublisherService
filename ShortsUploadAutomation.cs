using System.Diagnostics;

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

    public Task PublishShortAsync(
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
            return Task.CompletedTask;
        }

        // Open an Appium session so the configured device is reachable.
        // This method intentionally does not attempt a live upload.
        Connect();

        throw new InvalidOperationException(
            "LIVE_UPLOAD_NOT_IMPLEMENTED: UI selectors have not been verified. " +
            "No publication confirmation was issued.");
    }

    public string ReadCurrentScreen()
    {
        Connect();
        return _driver!.PageSource;
    }


    public string OpenYouTube()
    {
        Connect();
        EnsureYouTubeForeground();
        return _driver!.PageSource;
    }

    public string TestCreateButton()
    {
        OpenYouTube();

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

    public string TestSelectPublicVisibility()
    {
        Connect();

        // Проверенная координата пункта Public
        // в меню видимости Shorts на текущем эмуляторе.
        _driver!.ExecuteScript(
            "mobile: clickGesture",
            new Dictionary<string, object>
            {
                ["x"] = 540,
                ["y"] = 520
            });

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

    public string TestBackFromAudience()
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

    public string PublishVideoOnYouTubeAsync(
        string fileName,
        string caption)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("Video filename is required.", nameof(fileName));

        if (string.IsNullOrWhiteSpace(caption))
            throw new ArgumentException("Caption is required.", nameof(caption));

        // Appium's appPackage capability alone does not guarantee that YouTube
        // is the foreground app. Launch it through Android's package launcher
        // (monkey uses the package's launcher intent and avoids a fragile
        // hard-coded activity name containing '$'), then verify foreground.
        Connect();
        EnsureYouTubeForeground();
        Thread.Sleep(800);

        // 1. Открываем меню создания.
        var createButton = _driver!.FindElement(
            By.XPath("//*[@content-desc='Create' and @clickable='true']"));
        createButton.Click();
        Thread.Sleep(1000);

        // 2. Явно выбираем пункт Short. Нельзя нажимать creation_mode_button
        // вслепую: в некоторых версиях YouTube это переключатель Video, и такой
        // клик переводит поток в обычную загрузку видео.
        EnsureShortCreationMode();

        // 3. Открываем галерею только после подтверждения, что экран относится
        // к созданию Short. Если нужного элемента нет, прекращаем работу и
        // сохраняем XML экрана для диагностики — не продолжаем по Video-flow.
        var galleryButton = _driver!.FindElements(
                By.Id("com.google.android.youtube:id/unified_permissions_primary_button"))
            .FirstOrDefault(element => element.Displayed && element.Enabled);

        if (galleryButton == null)
        {
            throw CreateUiDiagnosticException(
                "После выбора Short не появился активный элемент Add from Gallery.");
        }

        var galleryButtonText = galleryButton.Text;
        var galleryButtonDescription = galleryButton.GetAttribute("content-desc") ?? string.Empty;
        if (!galleryButtonText.Contains("Gallery", StringComparison.OrdinalIgnoreCase) &&
            !galleryButtonDescription.Contains("Gallery", StringComparison.OrdinalIgnoreCase))
        {
            throw CreateUiDiagnosticException(
                $"Найден элемент галереи с неожиданной подписью: text='{galleryButtonText}', " +
                $"content-desc='{galleryButtonDescription}'.");
        }

        galleryButton.Click();
        Thread.Sleep(1200);

        // 4. Выбираем подготовленный файл.
        TestSelectGalleryVideo(fileName);

        // 5. Next из галереи.
        TestNextFromGallery();

        // 6. Done в редакторе Short.
        TestDoneFromEditor();

        // 7. Next на экране редактора Short.
        TestNextFromShortsEditor();

        // 8. Заполняем подпись.
        var captionScreen = TestSetCaption(caption);

        if (!captionScreen.Contains(caption, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Caption verification failed.");

        // 9. Открываем настройки видимости и выбираем Public.
        TestOpenFirstSettingsRow();
        TestSelectPublicVisibility();
        TestBackFromVisibility();

        // 10. Открываем настройки аудитории и выбираем
        // "No, it's not made for kids".
        TestOpenSelectAudience();
        TestSelectNotMadeForKids();
        TestBackFromAudience();

        // Возвращаем XML финального экрана настроек.
        // Кнопка Upload video здесь намеренно НЕ нажимается.
        return _driver.PageSource;
    }

    private void EnsureShortCreationMode()
    {
        const string galleryButtonId =
            "com.google.android.youtube:id/unified_permissions_primary_button";

        // Если уже открыт экран камеры Shorts, лишний раз ничего не нажимаем.
        if (_driver!.FindElements(By.Id(galleryButtonId))
            .Any(element => element.Displayed && element.Enabled))
        {
            return;
        }

        // Ищем только доступные для нажатия элементы с явным названием Short.
        // Это намеренно не использует creation_mode_button без проверки подписи.
        var candidates = _driver.FindElements(By.XPath(
            "//*[@clickable='true' and (" +
            "contains(translate(@text,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'short') or " +
            "contains(translate(@content-desc,'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz'),'short'))]"));

        var visibleCandidates = candidates
            .Where(element => element.Displayed && element.Enabled)
            .Select(element => new
            {
                Element = element,
                Text = (element.Text ?? string.Empty).Trim(),
                Description = (element.GetAttribute("content-desc") ?? string.Empty).Trim()
            })
            .Where(item =>
                item.Text.Contains("short", StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains("short", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item =>
            {
                var label = item.Text + " " + item.Description;
                if (label.Contains("create a short", StringComparison.OrdinalIgnoreCase)) return 0;
                if (label.Equals("short", StringComparison.OrdinalIgnoreCase) ||
                    label.Equals("shorts", StringComparison.OrdinalIgnoreCase)) return 1;
                if (label.Contains("short", StringComparison.OrdinalIgnoreCase)) return 2;
                return 3;
            })
            .ToList();

        if (visibleCandidates.Count == 0)
        {
            throw CreateUiDiagnosticException(
                "Не найден доступный для нажатия пункт Short. " +
                "Автоматизация не будет нажимать переключатель Video наугад.");
        }

        visibleCandidates[0].Element.Click();
        Thread.Sleep(1000);

        // Не считаем нажатие успешным, пока не появился элемент галереи Shorts.
        var galleryVisible = _driver.FindElements(By.Id(galleryButtonId))
            .Any(element => element.Displayed && element.Enabled);

        if (!galleryVisible)
        {
            throw CreateUiDiagnosticException(
                "Нажат пункт Short, но экран Shorts с кнопкой Add from Gallery не открылся.");
        }
    }

    private InvalidOperationException CreateUiDiagnosticException(string message)
    {
        string? diagnosticPath = null;
        string pageSource = string.Empty;
        try
        {
            pageSource = _driver?.PageSource ?? string.Empty;
            diagnosticPath = Path.Combine(
                Environment.CurrentDirectory,
                "youtube-ui-diagnostic.xml");
            File.WriteAllText(diagnosticPath, pageSource);
        }
        catch
        {
            // Основная ошибка важнее возможной ошибки записи диагностического XML.
        }

        string clickableSummary = string.Empty;
        try
        {
            clickableSummary = string.Join(
                Environment.NewLine,
                _driver!.FindElements(By.XPath("//*[@clickable='true']"))
                    .Where(element => element.Displayed)
                    .Select(element =>
                    {
                        var text = (element.Text ?? string.Empty).Trim();
                        var description = (element.GetAttribute("content-desc") ?? string.Empty).Trim();
                        var id = element.GetAttribute("resource-id") ?? string.Empty;
                        return $"text='{text}', desc='{description}', id='{id}'";
                    })
                    .Where(line => line != "text='', desc='', id=''")
                    .Take(40));
        }
        catch
        {
            clickableSummary = "Не удалось получить список clickable-элементов.";
        }

        var details = message + Environment.NewLine +
                      "Видимые clickable-элементы:" + Environment.NewLine + clickableSummary;
        if (!string.IsNullOrWhiteSpace(diagnosticPath))
            details += Environment.NewLine + $"XML экрана сохранён: {diagnosticPath}";

        return new InvalidOperationException(details);
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



    private const string YouTubePackage = "com.google.android.youtube";

    private void EnsureYouTubeForeground()
    {
        // Do not relaunch the app if Android already reports it as foreground.
        if (IsYouTubeForeground())
            return;

        var launch = RunAdb(
            "-s", _options.DeviceId,
            "shell", "monkey", "-p", YouTubePackage, "1");

        if (launch.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "ADB could not launch YouTube. " +
                $"ExitCode={launch.ExitCode}; stderr={launch.StdErr}; stdout={launch.StdOut}");
        }

        var deadline = DateTime.UtcNow.AddSeconds(20);
        string lastActivityOutput = string.Empty;

        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(500);
            lastActivityOutput = GetForegroundActivityOutput();

            if (IsForegroundOutputYouTube(lastActivityOutput))
            {
                // Give the accessibility tree time to settle after app startup.
                Thread.Sleep(800);
                return;
            }
        }

        throw new InvalidOperationException(
            "ADB ran the YouTube launch command, but YouTube did not become the " +
            "foreground app within 20 seconds. " +
            $"Last Android activity/window state: {lastActivityOutput}");
    }

    private bool IsYouTubeForeground()
    {
        return IsForegroundOutputYouTube(GetForegroundActivityOutput());
    }

    private string GetForegroundActivityOutput()
    {
        var activity = RunAdb(
            "-s", _options.DeviceId,
            "shell", "dumpsys", "activity", "activities");

        var window = RunAdb(
            "-s", _options.DeviceId,
            "shell", "dumpsys", "window", "windows");

        return activity.StdOut + Environment.NewLine + window.StdOut;
    }

    private static bool IsForegroundOutputYouTube(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return false;

        // Match only foreground/resumed activity markers, not a YouTube task
        // that may merely remain in Android's background task stack.
        return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(line =>
                (line.Contains("mResumedActivity", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("topResumedActivity", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("mCurrentFocus", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("mFocusedApp", StringComparison.OrdinalIgnoreCase)) &&
                line.Contains(YouTubePackage, StringComparison.OrdinalIgnoreCase));
    }

    private (int ExitCode, string StdOut, string StdErr) RunAdb(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.ExpandEnvironmentVariables(_options.AdbPath),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Could not start the configured ADB executable.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not start ADB executable '{_options.AdbPath}'. " +
                "Check Publisher:AdbPath in configuration.", ex);
        }

        // Drain both streams asynchronously to avoid a pipe-buffer deadlock.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(15000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException(
                $"ADB command timed out after 15 seconds: {string.Join(" ", arguments)}");
        }

        Task.WaitAll(stdoutTask, stderrTask);
        return (process.ExitCode, stdoutTask.Result, stderrTask.Result);
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
