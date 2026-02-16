using System.Diagnostics;
using System.Text;

namespace OnlyDFSMonitor.Core;

public sealed record ScCommandResult(int ExitCode, string StdOut, string StdErr)
{
    public bool IsSuccess => ExitCode == 0;

    public string CombinedOutput
    {
        get
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(StdOut))
            {
                builder.AppendLine(StdOut.Trim());
            }

            if (!string.IsNullOrWhiteSpace(StdErr))
            {
                builder.AppendLine(StdErr.Trim());
            }

            return builder.ToString().Trim();
        }
    }
}

public sealed record ServiceStatusInfo(bool Installed, bool Running, string RawStatus, ScCommandResult QueryResult);

public sealed class ServiceManager
{
    public async Task<int> RunScAsync(string arguments, CancellationToken ct)
        => (await RunScDetailedAsync(arguments, ct)).ExitCode;

    public async Task<ScCommandResult> RunScDetailedAsync(string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("sc.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Cannot start sc.exe");
        var stdOutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stdErrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return new ScCommandResult(process.ExitCode, await stdOutTask, await stdErrTask);
    }

    public async Task<bool> IsServiceInstalledAsync(string serviceName, CancellationToken ct)
        => (await QueryServiceAsync(serviceName, ct)).Installed;

    public Task<ScCommandResult> InstallServiceAsync(string serviceName, string binaryPath, bool autoStart, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(binaryPath))
        {
            throw new InvalidOperationException("Service executable path is required.");
        }

        var startMode = autoStart ? "auto" : "demand";
        return RunScDetailedAsync($"create {serviceName} binPath= \"{binaryPath}\" start= {startMode}", ct);
    }

    public Task<ScCommandResult> UninstallServiceAsync(string serviceName, CancellationToken ct)
        => RunScDetailedAsync($"delete {serviceName}", ct);

    public Task<ScCommandResult> StartServiceAsync(string serviceName, CancellationToken ct)
        => RunScDetailedAsync($"start {serviceName}", ct);

    public Task<ScCommandResult> StopServiceAsync(string serviceName, CancellationToken ct)
        => RunScDetailedAsync($"stop {serviceName}", ct);

    public async Task<ServiceStatusInfo> QueryServiceAsync(string serviceName, CancellationToken ct)
    {
        var result = await RunScDetailedAsync($"query {serviceName}", ct);
        var raw = result.CombinedOutput;

        if (result.ExitCode != 0)
        {
            return new ServiceStatusInfo(false, false, "Not installed", result);
        }

        var running = raw.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        var status = running
            ? "Running"
            : raw.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)
                ? "Stopped"
                : "Installed (unknown state)";

        return new ServiceStatusInfo(true, running, status, result);
    }
}
