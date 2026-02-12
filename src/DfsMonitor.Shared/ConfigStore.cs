using System.Text.Json;

namespace DfsMonitor.Shared;

public interface IConfigStore
{
    Task<MonitorConfig> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(MonitorConfig config, CancellationToken cancellationToken = default);
}

public sealed class UncConfigStore : IConfigStore
{
    private readonly string _configPath;
    private readonly string _cachePath;

    public UncConfigStore(string configPath, string localCacheRoot)
    {
        _configPath = configPath;
        Directory.CreateDirectory(localCacheRoot);
        _cachePath = Path.Combine(localCacheRoot, "config.cache.json");
    }

    public async Task<MonitorConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        var sourcePath = File.Exists(_configPath) ? _configPath : _cachePath;
        if (!File.Exists(sourcePath))
        {
            var defaultConfig = new MonitorConfig();
            await SaveAsync(defaultConfig, cancellationToken);
            return defaultConfig;
        }

        await using var stream = File.OpenRead(sourcePath);
        var config = await JsonSerializer.DeserializeAsync<MonitorConfig>(stream, JsonDefaults.Options, cancellationToken);
        return config ?? new MonitorConfig();
    }

    public async Task SaveAsync(MonitorConfig config, CancellationToken cancellationToken = default)
    {
        config.Version++;
        config.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var content = JsonSerializer.Serialize(config, JsonDefaults.Options);

        await AtomicWriteAsync(_cachePath, content, cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            if (File.Exists(_configPath))
            {
                var previousFile = Path.Combine(Path.GetDirectoryName(_configPath)!, $"config_{DateTime.UtcNow:yyyyMMddHHmmss}.json");
                File.Copy(_configPath, previousFile, overwrite: true);
            }
            await AtomicWriteAsync(_configPath, content, cancellationToken);
        }
        catch (IOException)
        {
            // UNC temporarily unavailable; cache already persisted locally.
        }
        catch (UnauthorizedAccessException)
        {
            // UNC temporarily unavailable or ACL issue; cache already persisted locally.
        }
    }

    private static async Task AtomicWriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temp, content, cancellationToken);
        File.Move(temp, path, overwrite: true);
    }
}

public interface IStatusStore
{
    Task SaveSnapshotAsync(CollectionSnapshot snapshot, string statusRoot, string localCacheRoot, CancellationToken cancellationToken = default);
    Task<CollectionSnapshot?> LoadLatestAsync(string statusRoot, string localCacheRoot, CancellationToken cancellationToken = default);
}

public sealed class StatusStore : IStatusStore
{
    public async Task SaveSnapshotAsync(CollectionSnapshot snapshot, string statusRoot, string localCacheRoot, CancellationToken cancellationToken = default)
    {
        var relative = Path.Combine($"{snapshot.CreatedAtUtc:yyyy}", $"{snapshot.CreatedAtUtc:MM}", $"{snapshot.CreatedAtUtc:dd}", "collector.json");
        var payload = JsonSerializer.Serialize(snapshot, JsonDefaults.Options);

        var localPath = Path.Combine(localCacheRoot, "status", relative);
        await WriteAtomicAsync(localPath, payload, cancellationToken);

        try
        {
            var uncPath = Path.Combine(statusRoot, relative);
            await WriteAtomicAsync(uncPath, payload, cancellationToken);
        }
        catch (IOException)
        {
            // UNC down.
        }
        catch (UnauthorizedAccessException)
        {
            // UNC inaccessible.
        }
    }

    public async Task<CollectionSnapshot?> LoadLatestAsync(string statusRoot, string localCacheRoot, CancellationToken cancellationToken = default)
    {
        var unc = FindLatest(Path.Combine(statusRoot));
        var fallback = FindLatest(Path.Combine(localCacheRoot, "status"));
        var latest = unc ?? fallback;
        if (latest is null)
        {
            return null;
        }

        var payload = await File.ReadAllTextAsync(latest, cancellationToken);
        return JsonSerializer.Deserialize<CollectionSnapshot>(payload, JsonDefaults.Options);
    }

    private static string? FindLatest(string root)
    {
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateFiles(root, "collector.json", SearchOption.AllDirectories)
            .OrderByDescending(x => x)
            .FirstOrDefault();
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temp, content, token);
        File.Move(temp, path, overwrite: true);
    }
}
