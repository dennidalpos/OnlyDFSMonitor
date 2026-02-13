using System.Diagnostics;

namespace OnlyDFSMonitor.Core;

public sealed class ServiceManager
{
    public async Task<int> RunScAsync(string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("sc.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Cannot start sc.exe");
        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }
}
