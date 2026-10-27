using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ShortsPublisher;

public sealed class AndroidDiagnostics(PublisherOptions options)
{
    public string SaveCurrentScreenXml(string? outputDirectory = null)
    {
        outputDirectory ??= Path.Combine(Directory.GetCurrentDirectory(), "diagnostics");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.GetFullPath(Path.Combine(outputDirectory,
            $"screen_{DateTime.Now:yyyyMMdd_HHmmss_fff}.xml"));
        const string remotePath = "/sdcard/shorts_publisher_screen.xml";
        var adb = Environment.ExpandEnvironmentVariables(options.AdbPath);
        (int Code, string Out, string Err) Run(params string[] args)
        {
            var psi = new ProcessStartInfo(adb) { UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var arg in args) psi.ArgumentList.Add(arg);
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("ADB failed to start");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("ADB command timed out");
            }
            Task.WaitAll(stdout, stderr);
            return (process.ExitCode, stdout.Result, stderr.Result);
        }
        string SaveViaAppium()
        {
            using var instagram = new InstagramPublisher(options);
            return instagram.SaveCurrentScreenXml(outputDirectory);
        }
        try
        {
            var dump = Run("-s", options.DeviceId, "shell", "uiautomator", "dump", remotePath);
            if (dump.Code != 0 || !dump.Out.Contains("dumped to", StringComparison.OrdinalIgnoreCase))
                return SaveViaAppium();
            var read = Run("-s", options.DeviceId, "shell", "cat", remotePath);
            if (read.Code != 0 || string.IsNullOrWhiteSpace(read.Out))
                return SaveViaAppium();
            try { XDocument.Parse(read.Out); }
            catch (System.Xml.XmlException) { return SaveViaAppium(); }
            File.WriteAllText(outputPath, read.Out, new System.Text.UTF8Encoding(false));
            return outputPath;
        }
        finally
        {
            try { Run("-s", options.DeviceId, "shell", "rm", "-f", remotePath); } catch { }
        }
    }
}
