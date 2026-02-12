using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management.Automation;
using DfsMonitor.Shared;

namespace DfsMonitor.Service;

public interface IDfsNamespaceCollector
{
    Task<List<NamespaceSnapshot>> CollectAsync(MonitorConfig config, CancellationToken cancellationToken);
}

public interface IDfsReplicationCollector
{
    Task<List<DfsrGroupSnapshot>> CollectAsync(MonitorConfig config, CancellationToken cancellationToken);
}

public sealed class DfsNamespaceCollector : IDfsNamespaceCollector
{
    private readonly ILogger<DfsNamespaceCollector> _logger;

    public DfsNamespaceCollector(ILogger<DfsNamespaceCollector> logger) => _logger = logger;

    public async Task<List<NamespaceSnapshot>> CollectAsync(MonitorConfig config, CancellationToken cancellationToken)
    {
        var results = new ConcurrentBag<NamespaceSnapshot>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = config.Collection.MaxParallelism, CancellationToken = cancellationToken };

        await Parallel.ForEachAsync(config.Namespaces.Where(x => x.Enabled), options, async (ns, ct) =>
        {
            var snapshot = new NamespaceSnapshot { NamespaceId = ns.Id, NamespacePath = ns.Path, LastCheckedUtc = DateTimeOffset.UtcNow, Health = HealthState.Ok };
            try
            {
                var folders = await QueryNamespaceFoldersAsync(ns.Path, ct);
                foreach (var folder in folders)
                {
                    var folderSnap = new NamespaceFolderSnapshot { FolderPath = folder.Path };
                    foreach (var target in folder.Targets)
                    {
                        var reachability = await CheckTargetAsync(target.UncPath, ct);
                        folderSnap.Targets.Add(new NamespaceTargetSnapshot
                        {
                            UncPath = target.UncPath,
                            Server = target.Server,
                            Share = target.Share,
                            PriorityClass = target.PriorityClass,
                            PriorityRank = target.PriorityRank,
                            Ordering = target.Ordering,
                            Reachable = reachability.reachable,
                            LatencyMs = reachability.latencyMs,
                            LastError = reachability.error,
                            LastCheckedUtc = DateTimeOffset.UtcNow
                        });
                    }
                    snapshot.Folders.Add(folderSnap);
                }

                var unreachableCount = snapshot.Folders.SelectMany(x => x.Targets).Count(x => !x.Reachable);
                snapshot.Health = unreachableCount >= config.Collection.Thresholds.CriticalUnreachableTargets
                    ? HealthState.Critical
                    : unreachableCount >= config.Collection.Thresholds.WarnUnreachableTargets ? HealthState.Warn : HealthState.Ok;
            }
            catch (Exception ex)
            {
                snapshot.Health = HealthState.Critical;
                _logger.LogError(ex, "Namespace collection failed for {Namespace}", ns.Path);
            }

            results.Add(snapshot);
        });

        return [..results];
    }

    private static async Task<(bool reachable, long? latencyMs, string? error)> CheckTargetAsync(string uncPath, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var parts = uncPath.TrimStart('\\').Split('\\');
            var server = parts.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(server)) return (false, null, "Invalid UNC path");

            await System.Net.Dns.GetHostEntryAsync(server, ct);
            var exists = await Task.Run(() => Directory.Exists(uncPath), ct);
            sw.Stop();
            return (exists, sw.ElapsedMilliseconds, exists ? null : "SMB path not reachable");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return (false, sw.ElapsedMilliseconds, ex.Message);
        }
    }

    private static async Task<List<DfsFolderResult>> QueryNamespaceFoldersAsync(string namespacePath, CancellationToken ct)
    {
        // TODO(Validation): Validate cmdlet output fields on target host.
        //   Get-DfsnFolder -Path "\\domain\namespace\*"
        //   Get-DfsnFolderTarget -Path "\\domain\namespace\folder"
        using var ps = PowerShell.Create();
        ps.AddScript($@"
$f = Get-DfsnFolder -Path '{namespacePath}\\*' -ErrorAction Stop
$rows = @()
foreach($folder in $f) {{
  $targets = Get-DfsnFolderTarget -Path $folder.Path -ErrorAction Continue
  foreach($t in $targets) {{
    $rows += [pscustomobject]@{{
      FolderPath = $folder.Path
      TargetPath = $t.TargetPath
      PriorityClass = [string]$t.ReferralPriorityClass
      PriorityRank = [int]$t.ReferralPriorityRank
      Ordering = [int]$t.State
    }}
  }}
}}
$rows | ConvertTo-Json -Depth 4
");

        var output = await Task.Run(() => ps.Invoke(), ct);
        if (ps.HadErrors || output.Count == 0)
        {
            return [];
        }

        var rows = System.Text.Json.JsonDocument.Parse(output[0].ToString() ?? "[]");
        var grouped = new Dictionary<string, DfsFolderResult>(StringComparer.OrdinalIgnoreCase);
        var items = rows.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array
            ? rows.RootElement.EnumerateArray().ToList()
            : new List<System.Text.Json.JsonElement> { rows.RootElement };

        foreach (var item in items)
        {
            var folder = item.GetProperty("FolderPath").GetString() ?? string.Empty;
            var target = item.GetProperty("TargetPath").GetString() ?? string.Empty;
            if (!grouped.TryGetValue(folder, out var folderResult))
            {
                folderResult = new DfsFolderResult { Path = folder };
                grouped[folder] = folderResult;
            }

            var targetParts = target.TrimStart('\\').Split('\\');
            folderResult.Targets.Add(new DfsTargetResult
            {
                UncPath = target,
                Server = targetParts.ElementAtOrDefault(0) ?? string.Empty,
                Share = targetParts.ElementAtOrDefault(1) ?? string.Empty,
                PriorityClass = item.TryGetProperty("PriorityClass", out var pc) ? pc.GetString() ?? "Unknown" : "Unknown",
                PriorityRank = item.TryGetProperty("PriorityRank", out var pr) && pr.TryGetInt32(out var p) ? p : null,
                Ordering = item.TryGetProperty("Ordering", out var ord) && ord.TryGetInt32(out var o) ? o : null
            });
        }

        return [.. grouped.Values];
    }

    private sealed class DfsFolderResult
    {
        public string Path { get; set; } = string.Empty;
        public List<DfsTargetResult> Targets { get; } = [];
    }

    private sealed class DfsTargetResult
    {
        public string UncPath { get; set; } = string.Empty;
        public string Server { get; set; } = string.Empty;
        public string Share { get; set; } = string.Empty;
        public string PriorityClass { get; set; } = "Unknown";
        public int? PriorityRank { get; set; }
        public int? Ordering { get; set; }
    }
}

public sealed class DfsReplicationCollector : IDfsReplicationCollector
{
    private readonly ILogger<DfsReplicationCollector> _logger;

    public DfsReplicationCollector(ILogger<DfsReplicationCollector> logger) => _logger = logger;

    public async Task<List<DfsrGroupSnapshot>> CollectAsync(MonitorConfig config, CancellationToken cancellationToken)
    {
        var groups = config.Dfsr.AutoDiscoverGroups ? await DiscoverGroupsAsync(cancellationToken) : config.Dfsr.ReplicationGroups;
        var bag = new ConcurrentBag<DfsrGroupSnapshot>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = config.Collection.MaxParallelism, CancellationToken = cancellationToken };

        await Parallel.ForEachAsync(groups, options, async (group, ct) =>
        {
            var result = new DfsrGroupSnapshot { GroupName = group, Health = HealthState.Ok };
            try
            {
                // TODO(Validation): Prefer Get-DfsrBacklog or dfsrdiag backlog against each connection in production.
                //   Get-DfsReplicationGroup -GroupName <name>
                //   Get-DfsrMember -GroupName <name>
                //   Get-WinEvent -LogName 'DFS Replication' -ComputerName <member>
                var members = await QueryMembersAsync(group, ct);
                result.Members.AddRange(members);
                result.Connections.AddRange(await QueryBacklogProxyAsync(members, ct));
                if (result.Members.Any(m => m.Health == HealthState.Critical) || result.Connections.Any(c => c.BacklogState == "Critical"))
                    result.Health = HealthState.Critical;
                else if (result.Members.Any(m => m.Health == HealthState.Warn) || result.Connections.Any(c => c.BacklogState == "Warn"))
                    result.Health = HealthState.Warn;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DFSR collection failed for group {Group}", group);
                result.Health = HealthState.Critical;
            }
            bag.Add(result);
        });

        return [..bag];
    }

    private static Task<List<string>> DiscoverGroupsAsync(CancellationToken _) => Task.FromResult(new List<string>());

    private static Task<List<DfsrMemberSnapshot>> QueryMembersAsync(string group, CancellationToken _) =>
        Task.FromResult(new List<DfsrMemberSnapshot>
        {
            new() { MemberName = $"{group}-member01", ServiceState = "Running", Health = HealthState.Ok },
            new() { MemberName = $"{group}-member02", ServiceState = "Unknown", Health = HealthState.Warn, RecentWarnings = ["TODO: remote event log query pending validation"] }
        });

    private static Task<List<DfsrConnectionSnapshot>> QueryBacklogProxyAsync(List<DfsrMemberSnapshot> members, CancellationToken _) =>
        Task.FromResult(members.Count < 2
            ? new List<DfsrConnectionSnapshot>()
            : new List<DfsrConnectionSnapshot>
            {
                new()
                {
                    SourceMember = members[0].MemberName,
                    DestinationMember = members[1].MemberName,
                    BacklogCount = null,
                    BacklogState = "Unknown",
                    Details = "Backlog probe requires Get-DfsrBacklog/dfsrdiag validation in target environment"
                }
            });
}
