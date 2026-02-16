using System.Diagnostics;
using System.Text.Json;

namespace OnlyDFSMonitor.Core;

public interface ISystemCommandRunner
{
    Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(string fileName, string arguments, CancellationToken ct);
}

public sealed class ProcessCommandRunner : ISystemCommandRunner
{
    public async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(string fileName, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Cannot start {fileName}");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }
}

public sealed class CollectionEngine
{
    private readonly ISystemCommandRunner _runner;

    public CollectionEngine() : this(new ProcessCommandRunner()) { }

    public CollectionEngine(ISystemCommandRunner runner) => _runner = runner;

    public async Task<Snapshot> CollectAsync(MonitorConfiguration config, CancellationToken ct)
    {
        var snapshot = new Snapshot();

        foreach (var ns in config.Namespaces.Where(n => n.Enabled))
        {
            ct.ThrowIfCancellationRequested();
            snapshot.Namespaces.Add(new NamespaceStatus
            {
                Path = ns.Path,
                Health = HealthState.Unknown,
                Targets = [new() { UncPath = ns.Path, Reachable = true }]
            });
        }

        var groups = config.DfsrGroups.Count > 0
            ? config.DfsrGroups
            : await DiscoverDfsrGroupsAsync(ct);

        foreach (var group in groups.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            snapshot.DfsrGroups.Add(new DfsrGroupStatus
            {
                GroupName = group,
                Health = HealthState.Unknown,
                Backlogs =
                [
                    new()
                    {
                        Source = "auto-source",
                        Destination = "auto-destination",
                        Folder = null,
                        BacklogCount = null,
                        Health = HealthState.Unknown,
                        Details = "DFSR group discovered automatically from host configuration."
                    }
                ]
            });
        }

        snapshot.OverallHealth = snapshot.Namespaces.Concat<object>(snapshot.DfsrGroups).Any() ? HealthState.Unknown : HealthState.Ok;
        return snapshot;
    }

    private async Task<List<string>> DiscoverDfsrGroupsAsync(CancellationToken ct)
    {
        var cmd = "Get-DfsReplicationGroup -ErrorAction SilentlyContinue | Select-Object -ExpandProperty GroupName | ConvertTo-Json -Depth 4";
        var escaped = cmd.Replace("\"", "\\\"");

        (int ExitCode, string StdOut, string StdErr) result;
        try
        {
            result = await _runner.RunAsync("pwsh", $"-NoProfile -Command \"{escaped}\"", ct);
        }
        catch
        {
            result = await _runner.RunAsync("powershell", $"-NoProfile -Command \"{escaped}\"", ct);
        }

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StdOut)) return [];

        try
        {
            using var doc = JsonDocument.Parse(result.StdOut);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.Array => doc.RootElement.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).ToList(),
                JsonValueKind.String => string.IsNullOrWhiteSpace(doc.RootElement.GetString()) ? [] : [doc.RootElement.GetString()!],
                _ => []
            };
        }
        catch
        {
            return [];
        }
    }
}
