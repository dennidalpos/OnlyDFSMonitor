using System.Text.Json;

namespace OnlyDFSMonitor.Core;

public sealed class JsonFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<T> LoadAsync<T>(string path, Func<T> fallback, CancellationToken ct)
    {
        if (!File.Exists(path)) return fallback();

        try
        {
            await using var stream = File.OpenRead(path);
            var model = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
            return model ?? fallback();
        }
        catch (JsonException)
        {
            return fallback();
        }
    }

    public async Task<T> LoadStrictAsync<T>(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("JSON file not found.", path);
        }

        T? model;
        try
        {
            await using var stream = File.OpenRead(path);
            model = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"File '{path}' contains invalid JSON payload.", ex);
        }

        if (model is null)
        {
            throw new InvalidDataException($"File '{path}' contains empty or invalid JSON payload.");
        }

        return model;
    }

    public async Task SaveAsync<T>(string path, T model, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, model, JsonOptions, ct);
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(tempPath, path);
    }
}
