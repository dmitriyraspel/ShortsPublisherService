using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;

namespace ShortsPublisher;

public sealed class InstagramPublisher(PublisherOptions options) : IDisposable
{
    private AndroidDriver? _driver;
    private readonly PublisherOptions _options = options;

    /// <summary>Opens Instagram using the existing Appium session.</summary>
    public string OpenInstagram()
    {
        Connect();
        _driver!.ActivateApp("com.instagram.android");
        Thread.Sleep(1500);
        return _driver.PageSource;
    }

    
    public void instagramHome()
    {
        // Кнопка Home в нижней панели Instagram.
        // Центр элемента: x = 108, y = 2274.
        RunAdb("shell", "input", "tap", "108", "2274");
    }

    // перед create обязательно нажать Home иначе интерфейс другой
    public void instagramCreate()
    {
        const string targetResourceId =
            "com.instagram.android:id/action_bar_left_button";

        const string remotePath = "/sdcard/shorts_publisher_screen.xml";

        try
        {
            // Получаем актуальную иерархию экрана.
            var dump = RunAdb(
                "-s", _options.DeviceId,
                "shell", "uiautomator", "dump", remotePath);

            if (dump.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Не удалось получить XML экрана: {dump.StdErr}");
            }

            var read = RunAdb(
                "-s", _options.DeviceId,
                "shell", "cat", remotePath);

            if (read.ExitCode != 0 ||
                string.IsNullOrWhiteSpace(read.StdOut))
            {
                throw new InvalidOperationException(
                    $"Не удалось прочитать XML экрана: {read.StdErr}");
            }

            var document = XDocument.Parse(read.StdOut);

            // Ищем элемент по ID, а не по видимой надписи.
            var matches = document
                .Descendants("node")
                .Where(node =>
                    (string?)node.Attribute("resource-id") == targetResourceId &&
                    (string?)node.Attribute("clickable") == "true" &&
                    (string?)node.Attribute("enabled") == "true")
                .ToList();

            if (matches.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Ожидался ровно один доступный элемент " +
                    $"{targetResourceId}, найдено: {matches.Count}. " +
                    "Нажатие отменено.");
            }

            var bounds = (string?)matches[0].Attribute("bounds");

            if (string.IsNullOrWhiteSpace(bounds))
            {
                throw new InvalidOperationException(
                    "У найденного элемента отсутствует bounds. " +
                    "Нажатие отменено.");
            }

            // Формат bounds: [x1,y1][x2,y2]
            var parts = bounds.Trim('[', ']').Split("][");

            if (parts.Length != 2)
            {
                throw new InvalidOperationException(
                    $"Не удалось разобрать bounds: {bounds}");
            }

            var topLeft = parts[0].Split(',');
            var bottomRight = parts[1].Split(',');

            if (topLeft.Length != 2 ||
                bottomRight.Length != 2 ||
                !int.TryParse(topLeft[0], out var x1) ||
                !int.TryParse(topLeft[1], out var y1) ||
                !int.TryParse(bottomRight[0], out var x2) ||
                !int.TryParse(bottomRight[1], out var y2))
            {
                throw new InvalidOperationException(
                    $"Некорректные координаты элемента: {bounds}");
            }

            if (x2 <= x1 || y2 <= y1)
            {
                throw new InvalidOperationException(
                    $"Пустая область элемента: {bounds}");
            }

            var centerX = x1 + (x2 - x1) / 2;
            var centerY = y1 + (y2 - y1) / 2;

            Console.WriteLine(
                $"Найдена кнопка {targetResourceId}, " +
                $"bounds={bounds}, нажимаем ({centerX}, {centerY}).");

            var tap = RunAdb(
                "-s", _options.DeviceId,
                "shell", "input", "tap",
                centerX.ToString(),
                centerY.ToString());

            if (tap.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Ошибка нажатия: {tap.StdErr}");
            }
        }
        finally
        {
            // Удаляем временный XML с устройства.
            RunAdb(
                "-s", _options.DeviceId,
                "shell", "rm", "-f", remotePath);
        }
    }


    public void instagramSelectReel()
    {
        // Получаем актуальную иерархию Android UI в памяти.
        // Локальный XML-файл не создаётся.
        const string remoteXmlPath = "/sdcard/shorts_publisher_screen.xml";

        var dump = RunAdb(
            "-s", _options.DeviceId,
            "shell", "uiautomator", "dump", remoteXmlPath);

        if (dump.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "Could not dump the current Android screen. " +
                $"ExitCode={dump.ExitCode}; stdout={dump.StdOut}; stderr={dump.StdErr}");
        }

        var read = RunAdb(
            "-s", _options.DeviceId,
            "shell", "cat", remoteXmlPath);

        // Удаляем временный XML с Android после чтения.
        RunAdb(
            "-s", _options.DeviceId,
            "shell", "rm", "-f", remoteXmlPath);

        if (read.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "Could not read the Android UI XML. " +
                $"ExitCode={read.ExitCode}; stderr={read.StdErr}");
        }

        if (string.IsNullOrWhiteSpace(read.StdOut))
        {
            throw new InvalidOperationException(
                "Android returned an empty UI hierarchy.");
        }

        var document = XDocument.Parse(read.StdOut);

        var reelNode = document
            .Descendants("node")
            .FirstOrDefault(node =>
                (string?)node.Attribute("resource-id") ==
                    "com.instagram.android:id/cam_dest_clips"
                && ((string?)node.Attribute("text") == "REEL"
                    || (string?)node.Attribute("content-desc") == "REEL")
                && (string?)node.Attribute("clickable") == "true"
                && (string?)node.Attribute("enabled") == "true");

        if (reelNode is null)
        {
            throw new InvalidOperationException(
                "Could not find an enabled, clickable REEL tab in the current Android UI.");
        }

        var bounds = (string?)reelNode.Attribute("bounds");
        var match = System.Text.RegularExpressions.Regex.Match(
            bounds ?? string.Empty,
            @"\[(\d+),(\d+)\]\[(\d+),(\d+)\]");

        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Could not parse REEL tab bounds: {bounds}");
        }

        int left = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        int top = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        int right = int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        int bottom = int.Parse(match.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);

        if (right <= left || bottom <= top)
        {
            throw new InvalidOperationException(
                $"REEL tab has invalid bounds: {bounds}");
        }

        int centerX = left + (right - left) / 2;
        int centerY = top + (bottom - top) / 2;

        var tap = RunAdb(
            "-s", _options.DeviceId,
            "shell", "input", "tap",
            centerX.ToString(System.Globalization.CultureInfo.InvariantCulture),
            centerY.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (tap.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "Could not tap the REEL tab. " +
                $"ExitCode={tap.ExitCode}; stdout={tap.StdOut}; stderr={tap.StdErr}");
        }
    }



    private (int ExitCode, string StdOut, string StdErr) RunAdb(params string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Environment.ExpandEnvironmentVariables(_options.AdbPath),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("ADB failed to start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("ADB timed out.");
        }
        Task.WaitAll(stdout, stderr);
        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    private void Connect()
    {
        if (_driver is not null) return;
        var caps = new AppiumOptions();
        caps.PlatformName = "Android";
        caps.AutomationName = "UiAutomator2";
        caps.DeviceName = "Pixel 6a";
        caps.AddAdditionalAppiumOption("udid", _options.DeviceId);
        caps.AddAdditionalAppiumOption("appPackage", "com.instagram.android");
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
