using System.Text.Json;

namespace OnlyDFSMonitor.Core;

public sealed class JsonFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<T> LoadAsync<T>(string path, Func<T> fallback, CancellationToken ct)
    {
        if (!File.Exists(path)) return fallback();
        await using var stream = File.OpenRead(path);
        var model = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
        return model ?? fallback();
    }

    public async Task SaveAsync<T>(string path, T model, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, model, JsonOptions, ct);
    }
}
