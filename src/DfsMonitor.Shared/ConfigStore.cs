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
    private readonly string _lockPath;

    public UncConfigStore(string configPath, string localCacheRoot)
    {
        _configPath = configPath;
        Directory.CreateDirectory(localCacheRoot);
        _cachePath = Path.Combine(localCacheRoot, "config.cache.json");
        _lockPath = $"{_configPath}.lock";
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
            await using var _ = await AcquireLockAsync(_lockPath, TimeSpan.FromSeconds(5), cancellationToken);

            if (File.Exists(_configPath))
            {
                var existing = await LoadFromPathAsync(_configPath, cancellationToken);
                if (existing is not null && existing.Version > config.Version)
                {
                    throw new InvalidOperationException($"Config update rejected due to version conflict. Existing={existing.Version}, Incoming={config.Version}");
                }

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

    private static async Task<MonitorConfig?> LoadFromPathAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<MonitorConfig>(stream, JsonDefaults.Options, cancellationToken);
    }

    private static async Task<FileStream> AcquireLockAsync(string lockPath, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var lockDir = Path.GetDirectoryName(lockPath);
                if (!string.IsNullOrWhiteSpace(lockDir)) Directory.CreateDirectory(lockDir);
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException)
            {
                await Task.Delay(150, cancellationToken);
            }
        }

        throw new TimeoutException($"Unable to acquire config lock: {lockPath}");
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
        var relative = Path.Combine($"{snapshot.CreatedAtUtc:yyyy}", $"{snapshot.CreatedAtUtc:MM}", $"{snapshot.CreatedAtUtc:dd}", $"collector-{snapshot.CreatedAtUtc:HHmmss}.json");
        var payload = JsonSerializer.Serialize(snapshot, JsonDefaults.Options);

        var localStatusRoot = Path.Combine(localCacheRoot, "status");
        var localPath = Path.Combine(localStatusRoot, relative);
        await WriteAtomicAsync(localPath, payload, cancellationToken);

        var pendingRoot = Path.Combine(localCacheRoot, "pending-sync");
        var pendingFile = Path.Combine(pendingRoot, relative.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_'));
        await WriteAtomicAsync(pendingFile, payload, cancellationToken);

        await TrySyncPendingAsync(statusRoot, pendingRoot, cancellationToken);
    }

    public async Task<CollectionSnapshot?> LoadLatestAsync(string statusRoot, string localCacheRoot, CancellationToken cancellationToken = default)
    {
        var unc = FindLatest(statusRoot);
        var fallback = FindLatest(Path.Combine(localCacheRoot, "status"));
        var latest = unc ?? fallback;
        if (latest is null)
        {
            return null;
        }

        var payload = await File.ReadAllTextAsync(latest, cancellationToken);
        return JsonSerializer.Deserialize<CollectionSnapshot>(payload, JsonDefaults.Options);
    }

    private static async Task TrySyncPendingAsync(string statusRoot, string pendingRoot, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(pendingRoot))
        {
            return;
        }

        foreach (var pending in Directory.EnumerateFiles(pendingRoot, "*.json", SearchOption.AllDirectories).OrderBy(x => x))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var payload = await File.ReadAllTextAsync(pending, cancellationToken);
                using var doc = JsonDocument.Parse(payload);
                var ts = doc.RootElement.GetProperty("createdAtUtc").GetDateTimeOffset();
                var relative = Path.Combine($"{ts:yyyy}", $"{ts:MM}", $"{ts:dd}", $"collector-{ts:HHmmss}.json");
                var uncPath = Path.Combine(statusRoot, relative);
                await WriteAtomicAsync(uncPath, payload, cancellationToken);
                File.Delete(pending);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    private static string? FindLatest(string root)
    {
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateFiles(root, "collector-*.json", SearchOption.AllDirectories)
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

public interface IRuntimeStateStore
{
    Task<RuntimeState> LoadAsync(string localCacheRoot, string runtimeStatePath, CancellationToken cancellationToken = default);
    Task SaveAsync(string localCacheRoot, string runtimeStatePath, RuntimeState state, CancellationToken cancellationToken = default);
}

public sealed class RuntimeStateStore : IRuntimeStateStore
{
    public async Task<RuntimeState> LoadAsync(string localCacheRoot, string runtimeStatePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Resolve(localCacheRoot, runtimeStatePath);
        if (!File.Exists(fullPath)) return new RuntimeState();

        var payload = await File.ReadAllTextAsync(fullPath, cancellationToken);
        return JsonSerializer.Deserialize<RuntimeState>(payload, JsonDefaults.Options) ?? new RuntimeState();
    }

    public async Task SaveAsync(string localCacheRoot, string runtimeStatePath, RuntimeState state, CancellationToken cancellationToken = default)
    {
        state.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var fullPath = Resolve(localCacheRoot, runtimeStatePath);
        var payload = JsonSerializer.Serialize(state, JsonDefaults.Options);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temp = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temp, payload, cancellationToken);
        File.Move(temp, fullPath, overwrite: true);
    }

    private static string Resolve(string localCacheRoot, string runtimeStatePath) =>
        Path.IsPathRooted(runtimeStatePath) ? runtimeStatePath : Path.Combine(localCacheRoot, runtimeStatePath);
}

public interface ICommandQueue
{
    Task EnqueueCollectNowAsync(MonitorConfig config, CollectNowCommand command, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CollectNowCommand>> DequeueCollectNowAsync(MonitorConfig config, CancellationToken cancellationToken = default);
}

public sealed class FileCommandQueue : ICommandQueue
{
    public async Task EnqueueCollectNowAsync(MonitorConfig config, CollectNowCommand command, CancellationToken cancellationToken = default)
    {
        var root = ResolveCommandRoot(config);
        Directory.CreateDirectory(root);
        var filePath = Path.Combine(root, $"collect-now-{command.RequestedAtUtc:yyyyMMddHHmmssfff}-{command.Id}.json");
        var payload = JsonSerializer.Serialize(command, JsonDefaults.Options);
        await File.WriteAllTextAsync(filePath, payload, cancellationToken);
    }

    public async Task<IReadOnlyList<CollectNowCommand>> DequeueCollectNowAsync(MonitorConfig config, CancellationToken cancellationToken = default)
    {
        var root = ResolveCommandRoot(config);
        if (!Directory.Exists(root)) return [];

        var commands = new List<CollectNowCommand>();
        foreach (var file in Directory.EnumerateFiles(root, "collect-now-*.json").OrderBy(x => x))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var payload = await File.ReadAllTextAsync(file, cancellationToken);
                var cmd = JsonSerializer.Deserialize<CollectNowCommand>(payload, JsonDefaults.Options);
                if (cmd is not null) commands.Add(cmd);
                File.Delete(file);
            }
            catch (IOException)
            {
                // keep file for next round
            }
            catch (UnauthorizedAccessException)
            {
                // keep file for next round
            }
        }

        return commands;
    }

    private static string ResolveCommandRoot(MonitorConfig config)
    {
        return Path.IsPathRooted(config.Storage.CommandQueuePath)
            ? config.Storage.CommandQueuePath
            : Path.Combine(config.Storage.LocalCacheRootPath, config.Storage.CommandQueuePath);
    }
}
