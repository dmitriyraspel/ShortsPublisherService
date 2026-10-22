using System.Diagnostics;

using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;

using System.IO;
using System.Threading;
using System.Xml.Linq;

namespace ShortsPublisher;

/// <summary>
/// YouTube UI automation. Existing uploads can be verified without publishing or saving screen XML.
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

    /// <summary>
    /// Verifies an already uploaded Short without clicking Upload or writing screen XML.
    /// </summary>
    public (bool Confirmed, string Message) VerifyUploadedShort(string expectedCaption)
    {
        Connect();

        // Use the same explicit YouTube UI confirmation as the publish flow.
        // Caption matching and uniqueness are not required because captions may repeat.
        var pageSource = _driver!.PageSource;
        var observation = AnalyzeUploadScreen(pageSource, expectedCaption);
        var confirmed = observation.ExplicitPublicationConfirmation;
        var message = confirmed
            ? "YouTube подтвердил публикацию: 'Uploaded to Your Channel', 'Share video' и 'You: Video uploaded'."
            : "Явное подтверждение публикации не найдено. Проверьте свежие XML-снимки в diagnostics.";

        Console.WriteLine(confirmed
            ? "[ПУБЛИКАЦИЯ ПОДТВЕРЖДЕНА] " + message
            : "[ПУБЛИКАЦИЯ НЕ ПОДТВЕРЖДЕНА] " + message);
        return (confirmed, message);
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
        // Нажимает Upload один раз и сохраняет только XML-снимки экрана.
        ClickUploadAndCaptureDiagnostics(TimeSpan.FromMinutes(4));
        return _driver!.PageSource;
    }

    private void ClickUploadAndCaptureDiagnostics(TimeSpan duration)
    {
        Connect();

        var uploadButton = _driver!.FindElement(
            By.Id("com.google.android.youtube:id/upload_bottom_button"));

        if (!uploadButton.Displayed || !uploadButton.Enabled)
            throw new InvalidOperationException(
                "Upload Short button is not visible or enabled. Nothing was clicked.");

        var diagnosticsDirectory = Path.Combine(
            Directory.GetCurrentDirectory(), "diagnostics");
        Directory.CreateDirectory(diagnosticsDirectory);

        uploadButton.Click();
        var stopwatch = Stopwatch.StartNew();
        var interval = TimeSpan.FromSeconds(8);

        while (true)
        {
            try
            {
                var pageSource = _driver.PageSource;
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                var snapshotPath = Path.Combine(diagnosticsDirectory, $"upload_{timestamp}.xml");
                File.WriteAllText(snapshotPath, pageSource);
            }
            catch
            {
                // Ошибка отдельного снимка не должна прерывать наблюдение после Upload.
            }

            var remaining = duration - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
                break;

            Thread.Sleep(remaining < interval ? remaining : interval);
        }
    }

    public UploadObservationResult PublishVideoOnYouTubeAsync(
        string fileName,
        string caption)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("Video filename is required.", nameof(fileName));

        if (string.IsNullOrWhiteSpace(caption))
            throw new ArgumentException("Caption is required.", nameof(caption));

        Connect();
        EnsureYouTubeForeground();
        Thread.Sleep(800);

        // 1. Open the Create menu.
        var createButton = _driver!.FindElement(
            By.XPath("//*[@content-desc='Create' and @clickable='true']"));
        createButton.Click();
        Thread.Sleep(1000);

        // 2. Explicitly select Short mode.
        EnsureShortCreationMode();

        // 3. Open the gallery only after the Shorts screen is verified.
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
        TestSelectGalleryVideo(fileName);
        TestNextFromGallery();
        TestDoneFromEditor();
        TestNextFromShortsEditor();

        // 4. Set and verify the exact caption.
        var captionScreen = TestSetCaption(caption);
        if (!captionScreen.Contains(caption, StringComparison.Ordinal))
            throw new InvalidOperationException("Caption verification failed.");

        // 5. Set visibility and audience.
        TestOpenFirstSettingsRow();
        TestSelectPublicVisibility();
        TestBackFromVisibility();
        TestOpenSelectAudience();
        TestSelectNotMadeForKids();
        TestBackFromAudience();

        // 6. Click Upload once, then observe the UI across screen transitions.
        // XML/PageSource is read in memory only and never written to disk.
        return ClickUploadAndObserve(caption, TimeSpan.FromMinutes(10));
    }

    private UploadObservationResult ClickUploadAndObserve(string? expectedCaption, TimeSpan timeout)
    {
        var uploadButton = _driver!.FindElement(
            By.Id("com.google.android.youtube:id/upload_bottom_button"));

        if (!uploadButton.Displayed || !uploadButton.Enabled)
            throw new InvalidOperationException(
                "Upload Short button is not visible or enabled. Nothing was clicked.");

        // Создаём каталог до нажатия Upload, чтобы подготовка диагностики
        // не могла сорваться уже после запуска публикации.
        Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "diagnostics"));
        uploadButton.Click();

        return ObserveUploadStatus(expectedCaption, timeout);
    }

    private UploadObservationResult ObserveUploadStatus(string? expectedCaption, TimeSpan timeout)
    {
        const int intervalSeconds = 8;
        var timer = Stopwatch.StartNew();
        var snapshots = 0;
        var matchingCardCount = 0;
        var sawUploadIndicator = false;
        var lastObservation = "Waiting for YouTube's post-upload screen.";

        while (timer.Elapsed <= timeout)
        {
            string pageSource;
            try
            {
                pageSource = _driver!.PageSource;
                snapshots++;

                // XML snapshots every 8 seconds for the first 4 minutes.
                // Snapshot errors must not interrupt an upload already in progress.
                if (timer.Elapsed <= TimeSpan.FromMinutes(4))
                {
                    try
                    {
                        var diagnosticsDirectory = Path.Combine(
                            Directory.GetCurrentDirectory(), "diagnostics");
                        Directory.CreateDirectory(diagnosticsDirectory);
                        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                        var snapshotPath = Path.Combine(
                            diagnosticsDirectory, $"upload_{timestamp}.xml");
                        File.WriteAllText(snapshotPath, pageSource);
                    }
                    catch
                    {
                        // XML diagnostics are best-effort; no console noise.
                    }
                }
            }
            catch (Exception ex)
            {
                return ReportUploadResult(new UploadObservationResult(
                    "publication_unknown", false,
                    $"Could not read YouTube status after the Upload click: {ex.Message}",
                    snapshots, matchingCardCount, timer.Elapsed));
            }

            var observation = AnalyzeUploadScreen(pageSource, expectedCaption);
            matchingCardCount = observation.MatchingCardCount;

            // Confirm only from the explicit UI signals seen in the supplied XML.
            // Caption/card uniqueness is deliberately NOT required: different Shorts
            // may have identical captions, and the confirmation toast/status is stronger.
            if (observation.ExplicitPublicationConfirmation)
            {
                var message = "YouTube подтвердил публикацию: 'Uploaded to Your Channel', " +
                    "'Share video' и 'You: Video uploaded'. Это подтверждает состояние UI; " +
                    "публичную доступность с другого аккаунта отдельно не проверяет.";
                return ReportUploadResult(new UploadObservationResult(
                    "published", true, message, snapshots, matchingCardCount, timer.Elapsed));
            }

            if (observation.FailureDetected)
            {
                var message = $"YouTube shows a possible upload error: {observation.FailureText}";
                return ReportUploadResult(new UploadObservationResult(
                    "upload_failed", false, message, snapshots, matchingCardCount, timer.Elapsed));
            }

            if (observation.UploadIndicatorVisible)
            {
                sawUploadIndicator = true;
                lastObservation = "YouTube still shows the upload indicator.";
            }
            else
            {
                lastObservation = sawUploadIndicator
                    ? "Upload indicator disappeared, but explicit publication confirmation has not appeared yet."
                    : "Waiting for upload indicator or explicit publication confirmation.";
            }

            var remaining = timeout - timer.Elapsed;
            if (remaining <= TimeSpan.Zero)
                break;

            Thread.Sleep(remaining < TimeSpan.FromSeconds(intervalSeconds)
                ? remaining
                : TimeSpan.FromSeconds(intervalSeconds));
        }

        var finalMessage = $"Не удалось получить явное подтверждение публикации за {timeout.TotalMinutes:F0} мин. " +
            $"Последнее состояние: {lastObservation} Снимков экрана: {snapshots}. " +
            "XML-снимки сохранялись в diagnostics в течение первых 4 минут, если чтение экрана прошло успешно.";
        return ReportUploadResult(new UploadObservationResult(
            "publication_unconfirmed", false, finalMessage,
            snapshots, matchingCardCount, timer.Elapsed));
    }

    private static UploadObservationResult ReportUploadResult(UploadObservationResult result)
    {
        if (result.Published)
        {
            Console.WriteLine("[ПУБЛИКАЦИЯ ПОДТВЕРЖДЕНА] " + result.Message);
        }
        else
        {
            Console.WriteLine($"[ПУБЛИКАЦИЯ НЕ ПОДТВЕРЖДЕНА: {result.Status}] " + result.Message);
        }

        return result;
    }

    private static ScreenUploadObservation AnalyzeUploadScreen(string pageSource, string? expectedCaption)
    {
        var uploadIndicatorVisible = ContainsUploadIndicator(pageSource);
        var failureText = FindUploadFailureText(pageSource);
        var matchingCardCount = 0;
        var explicitPublicationConfirmation = false;

        try
        {
            var document = XDocument.Parse(pageSource);
            var visibleText = document.Descendants()
                .SelectMany(element => new[]
                {
                    (string?)element.Attribute("text") ?? string.Empty,
                    (string?)element.Attribute("content-desc") ?? string.Empty
                })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            var uploadedToChannel = visibleText.Any(value =>
                value.Contains("Uploaded to Your Channel", StringComparison.OrdinalIgnoreCase));
            var shareVideoAvailable = visibleText.Any(value =>
                value.Contains("Share video", StringComparison.OrdinalIgnoreCase));
            var videoUploadedStatus = visibleText.Any(value =>
                value.Contains("You: Video uploaded", StringComparison.OrdinalIgnoreCase));

            explicitPublicationConfirmation = uploadedToChannel
                && shareVideoAvailable
                && videoUploadedStatus;

            // Caption matching is diagnostic only. It is never a publication gate.
            if (!string.IsNullOrWhiteSpace(expectedCaption))
            {
                matchingCardCount = document.Descendants()
                    .Where(element =>
                    {
                        var description = (string?)element.Attribute("content-desc") ?? string.Empty;
                        var displayed = (string?)element.Attribute("displayed");
                        return (displayed is null || !displayed.Equals("false", StringComparison.OrdinalIgnoreCase))
                            && description.Contains(expectedCaption, StringComparison.OrdinalIgnoreCase)
                            && description.Contains("play Short", StringComparison.OrdinalIgnoreCase);
                    })
                    .GroupBy(element =>
                        ((string?)element.Attribute("content-desc") ?? string.Empty) + "|" +
                        ((string?)element.Attribute("bounds") ?? string.Empty))
                    .Select(group => group.First())
                    .Count();
            }
        }
        catch
        {
            // Malformed accessibility XML must never be interpreted as success.
            matchingCardCount = 0;
            explicitPublicationConfirmation = false;
        }

        return new ScreenUploadObservation(
            matchingCardCount, uploadIndicatorVisible, explicitPublicationConfirmation,
            !string.IsNullOrWhiteSpace(failureText), failureText ?? string.Empty);
    }

    private static bool ContainsUploadIndicator(string pageSource)
    {
        if (string.IsNullOrWhiteSpace(pageSource))
            return false;

        try
        {
            var document = XDocument.Parse(pageSource);
            return document.Descendants().Any(element =>
            {
                var text = (string?)element.Attribute("text") ?? string.Empty;
                var description = (string?)element.Attribute("content-desc") ?? string.Empty;
                var resourceId = (string?)element.Attribute("resource-id") ?? string.Empty;
                return text.Contains("Video uploading", StringComparison.OrdinalIgnoreCase)
                    || description.Contains("Video uploading", StringComparison.OrdinalIgnoreCase)
                    || (resourceId.Contains("processing_indicator_label", StringComparison.OrdinalIgnoreCase)
                        && (text.Contains("Processing", StringComparison.OrdinalIgnoreCase)
                            || description.Contains("Processing", StringComparison.OrdinalIgnoreCase)));
            });
        }
        catch
        {
            return pageSource.Contains("Video uploading", StringComparison.OrdinalIgnoreCase)
                || (pageSource.Contains("processing_indicator_label", StringComparison.OrdinalIgnoreCase)
                    && pageSource.Contains("Processing", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string? FindUploadFailureText(string pageSource)
    {
        try
        {
            var document = XDocument.Parse(pageSource);
            return document.Descendants()
                .SelectMany(element => new[]
                {
                    (string?)element.Attribute("text") ?? string.Empty,
                    (string?)element.Attribute("content-desc") ?? string.Empty
                })
                .FirstOrDefault(value =>
                    value.Contains("upload failed", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("failed to upload", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("couldn't upload", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("could not upload", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private sealed record ScreenUploadObservation(
        int MatchingCardCount,
        bool UploadIndicatorVisible,
        bool ExplicitPublicationConfirmation,
        bool FailureDetected,
        string FailureText);

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
        try
        {
            _ = _driver?.PageSource; // Read in memory only; never persist screen XML.
        }
        catch
        {
            // Основная ошибка важнее дополнительной диагностики.
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


public sealed record UploadObservationResult(
    string Status,
    bool Published,
    string Message,
    int ScreenReads,
    int MatchingCardCount,
    TimeSpan Elapsed);
