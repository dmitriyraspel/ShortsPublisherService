using System.Diagnostics;

namespace ShortsPublisher;

public sealed class AdbClient(string adbPath)
{
    private readonly string _adbPath = Environment.ExpandEnvironmentVariables(adbPath);

    public async Task<string> RunAsync(IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(_adbPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Cannot start ADB at '{_adbPath}'. Check Publisher:AdbPath.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ADB exited with code {process.ExitCode}: {stderr.Trim()}");
        return stdout.Trim();
    }

    public Task<string> DevicesAsync(CancellationToken ct) =>
        RunAsync(new[] { "devices" }, ct);

    public Task PushAsync(string deviceId, string local, string remote, CancellationToken ct) =>
        RunAsync(new[] { "-s", deviceId, "push", local, remote }, ct);

    public Task<string> ShellAsync(string deviceId, CancellationToken ct, params string[] shellArgs) =>
        RunAsync(new[] { "-s", deviceId, "shell" }.Concat(shellArgs), ct);
}
