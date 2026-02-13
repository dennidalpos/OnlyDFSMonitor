using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management.Automation;
using System.Text.Json;
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
                var folders = await ExecuteWithRetriesAsync(config.Collection.RetryCount, ct, () => QueryNamespaceFoldersAsync(ns.Path, config.Collection.RequestTimeoutSeconds, ct));
                foreach (var folder in folders)
                {
                    var folderSnap = new NamespaceFolderSnapshot { FolderPath = folder.Path };
                    foreach (var target in folder.Targets)
                    {
                        var reachability = await CheckTargetAsync(target.UncPath, config.Collection.RequestTimeoutSeconds, ct);
                        folderSnap.Targets.Add(new NamespaceTargetSnapshot
                        {
                            UncPath = target.UncPath,
                            Server = target.Server,
                            Share = target.Share,
                            PriorityClass = target.PriorityClass,
                            PriorityRank = target.PriorityRank,
                            Ordering = target.Ordering,
                            State = target.StateRaw,
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

    private static async Task<(bool reachable, long? latencyMs, string? error)> CheckTargetAsync(string uncPath, int timeoutSeconds, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(2, timeoutSeconds)));

        try
        {
            var parts = uncPath.TrimStart('\\').Split('\\');
            var server = parts.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(server)) return (false, null, "Invalid UNC path");

            await System.Net.Dns.GetHostEntryAsync(server, cts.Token);
            var exists = await Task.Run(() => Directory.Exists(uncPath), cts.Token);
            sw.Stop();
            return (exists, sw.ElapsedMilliseconds, exists ? null : "SMB path not reachable");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return (false, sw.ElapsedMilliseconds, ex.Message);
        }
    }

    private static async Task<List<DfsFolderResult>> QueryNamespaceFoldersAsync(string namespacePath, int timeoutSeconds, CancellationToken ct)
    {
        var script = $@"
$f = Get-DfsnFolder -Path '{namespacePath}\\*' -ErrorAction Stop
$rows = @()
foreach($folder in $f) {{
  $targets = Get-DfsnFolderTarget -Path $folder.Path -ErrorAction Continue
  foreach($t in $targets) {{
    $enabled = if($null -ne $t.State -and [string]$t.State -match 'Online|Enabled|1') {{ 1 }} else {{ 0 }}
    $ranking = if($null -ne $t.ReferralPriorityRank) {{ [int]$t.ReferralPriorityRank }} else {{ 9999 }}
    $classWeight = switch -Regex ([string]$t.ReferralPriorityClass) {{
      'GlobalHigh' {{ 0; break }}
      'SiteCostHigh' {{ 1; break }}
      'SiteCostNormal' {{ 2; break }}
      'GlobalLow' {{ 3; break }}
      default {{ 4; break }}
    }}

    $rows += [pscustomobject]@{{
      FolderPath = $folder.Path
      TargetPath = $t.TargetPath
      PriorityClass = [string]$t.ReferralPriorityClass
      PriorityRank = [int]$t.ReferralPriorityRank
      StateRaw = [string]$t.State
      Ordering = [int](($enabled * 100000) + ((5 - $classWeight) * 1000) - $ranking)
    }}
  }}
}}
$rows | ConvertTo-Json -Depth 5
";

        var json = await InvokePowerShellJsonAsync(script, timeoutSeconds, ct);
        if (string.IsNullOrWhiteSpace(json)) return [];

        using var doc = JsonDocument.Parse(json);
        var grouped = new Dictionary<string, DfsFolderResult>(StringComparer.OrdinalIgnoreCase);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().ToList()
            : new List<JsonElement> { doc.RootElement };

        foreach (var item in items)
        {
            var folder = item.TryGetProperty("FolderPath", out var fp) ? fp.GetString() ?? string.Empty : string.Empty;
            var target = item.TryGetProperty("TargetPath", out var tp) ? tp.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(target)) continue;

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
                Ordering = item.TryGetProperty("Ordering", out var ord) && ord.TryGetInt32(out var o) ? o : null,
                StateRaw = item.TryGetProperty("StateRaw", out var stateRaw) ? stateRaw.GetString() : null
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
        public string? StateRaw { get; set; }
    }

    private static async Task<T> ExecuteWithRetriesAsync<T>(int retries, CancellationToken ct, Func<Task<T>> run)
    {
        Exception? last = null;
        for (var attempt = 0; attempt <= Math.Max(0, retries); attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await run();
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), ct);
            }
        }

        throw last ?? new InvalidOperationException("Unknown retry failure");
    }

    private static async Task<string?> InvokePowerShellJsonAsync(string script, int timeoutSeconds, CancellationToken ct)
    {
        using var ps = PowerShell.Create();
        ps.AddScript(script);
        var invokeTask = Task.Run(() => ps.Invoke(), ct);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(Math.Max(2, timeoutSeconds)), ct);
        var completed = await Task.WhenAny(invokeTask, timeoutTask);
        if (completed == timeoutTask)
        {
            ps.Stop();
            throw new TimeoutException("PowerShell command timed out");
        }

        var output = await invokeTask;
        if (ps.HadErrors || output.Count == 0)
        {
            return null;
        }

        return output[0].ToString();
    }
}

public sealed class DfsReplicationCollector : IDfsReplicationCollector
{
    private readonly ILogger<DfsReplicationCollector> _logger;

    public DfsReplicationCollector(ILogger<DfsReplicationCollector> logger) => _logger = logger;

    public async Task<List<DfsrGroupSnapshot>> CollectAsync(MonitorConfig config, CancellationToken cancellationToken)
    {
        var groups = config.Dfsr.AutoDiscoverGroups
            ? await DiscoverGroupsAsync(config.Collection.RequestTimeoutSeconds, cancellationToken)
            : config.Dfsr.ReplicationGroups;

        var bag = new ConcurrentBag<DfsrGroupSnapshot>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = config.Collection.MaxParallelism, CancellationToken = cancellationToken };

        await Parallel.ForEachAsync(groups.Distinct(StringComparer.OrdinalIgnoreCase), options, async (group, ct) =>
        {
            var result = new DfsrGroupSnapshot { GroupName = group, Health = HealthState.Ok };
            try
            {
                var members = await QueryMembersAsync(group, config.Collection.EventLogSampleCount, config.Collectors.EventLog, config.Collection.RequestTimeoutSeconds, ct);
                result.Members.AddRange(members);

                var connections = await QueryConnectionsAsync(group, config.Collection.RequestTimeoutSeconds, ct);
                var replicatedFolders = await QueryReplicatedFoldersAsync(group, config.Collection.RequestTimeoutSeconds, ct);
                result.Connections.AddRange(await QueryBacklogAsync(group, members, connections, replicatedFolders, config.Collection.Thresholds, config.Collection.RequestTimeoutSeconds, ct));

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

        return [.. bag];
    }

    private static async Task<List<string>> DiscoverGroupsAsync(int timeoutSeconds, CancellationToken ct)
    {
        const string script = "Get-DfsReplicationGroup -ErrorAction Stop | Select-Object -ExpandProperty GroupName | ConvertTo-Json -Depth 3";
        var json = await InvokePowerShellJsonAsync(script, timeoutSeconds, ct);
        if (string.IsNullOrWhiteSpace(json)) return [];

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            return doc.RootElement.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        }

        var single = doc.RootElement.GetString();
        return string.IsNullOrWhiteSpace(single) ? [] : [single];
    }

    private static async Task<List<DfsrMemberSnapshot>> QueryMembersAsync(string groupName, int eventLimit, bool includeEventLog, int timeoutSeconds, CancellationToken ct)
    {
        var script = $@"
$members = Get-DfsrMember -GroupName '{groupName}' -ErrorAction Stop
$rows = @()
foreach($m in $members) {{
  $service = Get-Service -Name DFSR -ComputerName $m.ComputerName -ErrorAction SilentlyContinue
  $warnings = @()
  if ({(includeEventLog ? "$true" : "$false")}) {{
    $warnings = Get-WinEvent -LogName 'DFS Replication' -ComputerName $m.ComputerName -MaxEvents {eventLimit} -ErrorAction SilentlyContinue |
      Where-Object {{$_.LevelDisplayName -in @('Warning','Error','Critical')}} |
      Select-Object -First 5 -ExpandProperty Message
  }}

  $rows += [pscustomobject]@{{
    MemberName = $m.ComputerName
    ServiceState = if($service) {{ [string]$service.Status }} else {{ 'Unknown' }}
    Warnings = $warnings
  }}
}}
$rows | ConvertTo-Json -Depth 5
";

        var json = await InvokePowerShellJsonAsync(script, timeoutSeconds, ct);
        if (string.IsNullOrWhiteSpace(json)) return [];

        using var doc = JsonDocument.Parse(json);
        var rows = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().ToList()
            : new List<JsonElement> { doc.RootElement };

        var results = new List<DfsrMemberSnapshot>();
        foreach (var row in rows)
        {
            var serviceState = row.TryGetProperty("ServiceState", out var ss) ? ss.GetString() ?? "Unknown" : "Unknown";
            var warnings = new List<string>();
            if (row.TryGetProperty("Warnings", out var ws))
            {
                if (ws.ValueKind == JsonValueKind.Array) warnings = ws.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                else if (ws.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(ws.GetString())) warnings.Add(ws.GetString()!);
            }

            var health = serviceState.Equals("Running", StringComparison.OrdinalIgnoreCase)
                ? (warnings.Count > 0 ? HealthState.Warn : HealthState.Ok)
                : HealthState.Critical;

            results.Add(new DfsrMemberSnapshot
            {
                MemberName = row.TryGetProperty("MemberName", out var mn) ? mn.GetString() ?? string.Empty : string.Empty,
                ServiceState = serviceState,
                Health = health,
                RecentWarnings = warnings
            });
        }

        return results;
    }

    private static async Task<List<(string Source, string Destination)>> QueryConnectionsAsync(string groupName, int timeoutSeconds, CancellationToken ct)
    {
        var script = $@"
$connections = Get-DfsrConnection -GroupName '{groupName}' -ErrorAction SilentlyContinue
$connections | ForEach-Object {{
  [pscustomobject]@{{
    Source = [string]$_.SourceComputerName
    Destination = [string]$_.DestinationComputerName
  }}
}} | ConvertTo-Json -Depth 4
";

        var json = await InvokePowerShellJsonAsync(script, timeoutSeconds, ct);
        if (string.IsNullOrWhiteSpace(json)) return [];

        using var doc = JsonDocument.Parse(json);
        var rows = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().ToList()
            : new List<JsonElement> { doc.RootElement };

        var results = new List<(string Source, string Destination)>();
        foreach (var row in rows)
        {
            var source = row.TryGetProperty("Source", out var src) ? src.GetString() ?? string.Empty : string.Empty;
            var destination = row.TryGetProperty("Destination", out var dst) ? dst.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination)) continue;
            results.Add((source, destination));
        }

        return results;
    }

    private static async Task<List<string>> QueryReplicatedFoldersAsync(string groupName, int timeoutSeconds, CancellationToken ct)
    {
        var script = $@"
Get-DfsReplicatedFolder -GroupName '{groupName}' -ErrorAction SilentlyContinue |
  Select-Object -ExpandProperty FolderName |
  ConvertTo-Json -Depth 4
";

        var json = await InvokePowerShellJsonAsync(script, timeoutSeconds, ct);
        if (string.IsNullOrWhiteSpace(json)) return [];

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            return doc.RootElement
                .EnumerateArray()
                .Select(x => x.GetString() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var folder = doc.RootElement.GetString();
        return string.IsNullOrWhiteSpace(folder) ? [] : [folder];
    }

    private static async Task<List<DfsrConnectionSnapshot>> QueryBacklogAsync(string groupName, List<DfsrMemberSnapshot> members, List<(string Source, string Destination)> connections, List<string> replicatedFolders, ThresholdOptions thresholds, int timeoutSeconds, CancellationToken ct)
    {
        if (members.Count < 2) return [];

        var normalizedMembers = members
            .Select(x => x.MemberName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (connections.Count == 0)
        {
            for (var i = 0; i < normalizedMembers.Count - 1; i++)
            {
                connections.Add((normalizedMembers[i], normalizedMembers[i + 1]));
            }
        }

        var distinctConnections = connections
            .DistinctBy(x => $"{x.Source}->{x.Destination}", StringComparer.OrdinalIgnoreCase)
            .ToList();

        var folders = replicatedFolders.Count == 0 ? new List<string?> { null } : [.. replicatedFolders.Cast<string?>()];

        var list = new List<DfsrConnectionSnapshot>();
        foreach (var connection in distinctConnections)
        {
            var src = connection.Source;
            var dst = connection.Destination;

            foreach (var folderName in folders)
            {
                var backlogResult = await QuerySingleBacklogAsync(groupName, src, dst, folderName, timeoutSeconds, ct);
                var state = backlogResult.State;
                if (backlogResult.BacklogCount.HasValue)
                {
                    state = backlogResult.BacklogCount.Value >= thresholds.CriticalBacklog ? "Critical" : backlogResult.BacklogCount.Value >= thresholds.WarnBacklog ? "Warn" : "Ok";
                }

                list.Add(new DfsrConnectionSnapshot
                {
                    SourceMember = src,
                    DestinationMember = dst,
                    ReplicatedFolder = folderName,
                    BacklogCount = backlogResult.BacklogCount,
                    BacklogState = state,
                    Details = backlogResult.Details
                });
            }
        }

        return list;
    }

    private static async Task<(int? BacklogCount, string State, string? Details)> QuerySingleBacklogAsync(string groupName, string src, string dst, string? folderName, int timeoutSeconds, CancellationToken ct)
    {
        var folderParameter = string.IsNullOrWhiteSpace(folderName)
            ? string.Empty
            : $" -FolderName '{folderName.Replace("'", "''")}'";

        var script = $@"
try {{
  $b = Get-DfsrBacklog -GroupName '{groupName}' -SourceComputerName '{src}' -DestinationComputerName '{dst}'{folderParameter} -ErrorAction Stop
  [pscustomobject]@{{ BacklogCount = [int]$b.BacklogFileCount; State='Known'; Details='' }} | ConvertTo-Json -Depth 3
}} catch {{
  [pscustomobject]@{{ BacklogCount = $null; State='Unknown'; Details=$_.Exception.Message }} | ConvertTo-Json -Depth 3
}}
";

        var json = await InvokePowerShellJsonAsync(script, timeoutSeconds, ct);
        if (string.IsNullOrWhiteSpace(json)) return (null, "Unknown", "No backlog data");

        using var doc = JsonDocument.Parse(json);
        int? backlog = null;
        if (doc.RootElement.TryGetProperty("BacklogCount", out var bc) && bc.TryGetInt32(out var bcv)) backlog = bcv;

        var state = doc.RootElement.TryGetProperty("State", out var st) ? st.GetString() ?? "Unknown" : "Unknown";
        var details = doc.RootElement.TryGetProperty("Details", out var dt) ? dt.GetString() : null;

        if (!string.IsNullOrWhiteSpace(folderName) && !string.IsNullOrWhiteSpace(details))
        {
            details = $"Folder {folderName}: {details}";
        }

        return (backlog, state, details);
    }

    private static async Task<string?> InvokePowerShellJsonAsync(string script, int timeoutSeconds, CancellationToken ct)
    {
        using var ps = PowerShell.Create();
        ps.AddScript(script);
        var invokeTask = Task.Run(() => ps.Invoke(), ct);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(Math.Max(2, timeoutSeconds)), ct);
        var completed = await Task.WhenAny(invokeTask, timeoutTask);
        if (completed == timeoutTask)
        {
            ps.Stop();
            throw new TimeoutException("PowerShell command timed out");
        }

        var output = await invokeTask;
        if (ps.HadErrors || output.Count == 0) return null;
        return output[0].ToString();
    }
}
