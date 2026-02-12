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
    $rows += [pscustomobject]@{{
      FolderPath = $folder.Path
      TargetPath = $t.TargetPath
      PriorityClass = [string]$t.ReferralPriorityClass
      PriorityRank = [int]$t.ReferralPriorityRank
      Ordering = [int]$t.State
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

                var memberNames = members.Select(x => x.MemberName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                result.Connections.AddRange(await QueryBacklogAsync(group, memberNames, config.Collection.Thresholds, config.Collection.RequestTimeoutSeconds, ct));

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

    private static async Task<List<DfsrConnectionSnapshot>> QueryBacklogAsync(string groupName, List<string> members, ThresholdOptions thresholds, int timeoutSeconds, CancellationToken ct)
    {
        if (members.Count < 2) return [];

        var list = new List<DfsrConnectionSnapshot>();
        for (var i = 0; i < members.Count - 1; i++)
        {
            var src = members[i];
            var dst = members[i + 1];
            var script = $@"
try {{
  $b = Get-DfsrBacklog -GroupName '{groupName}' -SourceComputerName '{src}' -DestinationComputerName '{dst}' -ErrorAction Stop
  [pscustomobject]@{{ BacklogCount = [int]$b.BacklogFileCount; State='Known'; Details='' }} | ConvertTo-Json -Depth 3
}} catch {{
  [pscustomobject]@{{ BacklogCount = $null; State='Unknown'; Details=$_.Exception.Message }} | ConvertTo-Json -Depth 3
}}
";

            var json = await InvokePowerShellJsonAsync(script, timeoutSeconds, ct);
            int? backlog = null;
            var state = "Unknown";
            string? details = null;

            if (!string.IsNullOrWhiteSpace(json))
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("BacklogCount", out var bc) && bc.TryGetInt32(out var bcv)) backlog = bcv;
                state = doc.RootElement.TryGetProperty("State", out var st) ? st.GetString() ?? "Unknown" : "Unknown";
                details = doc.RootElement.TryGetProperty("Details", out var dt) ? dt.GetString() : null;
            }

            if (backlog.HasValue)
            {
                state = backlog.Value >= thresholds.CriticalBacklog ? "Critical" : backlog.Value >= thresholds.WarnBacklog ? "Warn" : "Ok";
            }

            list.Add(new DfsrConnectionSnapshot
            {
                SourceMember = src,
                DestinationMember = dst,
                BacklogCount = backlog,
                BacklogState = state,
                Details = details
            });
        }

        return list;
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
