using System.Text.Json;

namespace KeyLockr.Core.Configuration;

public sealed class KeyLockrConfigurationStore
{
    private readonly string _configPath;

    public KeyLockrConfigurationStore(string? overridePath = null)
    {
        _configPath = overridePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyLockr", "config.json");
    }

    public string ConfigurationPath => _configPath;

    public async Task<KeyLockrConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_configPath))
        {
            return KeyLockrConfiguration.CreateDefault();
        }

        await using var stream = File.OpenRead(_configPath);
        var configuration = await JsonSerializer.DeserializeAsync<KeyLockrConfiguration>(stream, GetSerializerOptions(), cancellationToken);
        return configuration ?? KeyLockrConfiguration.CreateDefault();
    }

    public async Task SaveAsync(KeyLockrConfiguration configuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_configPath);
        await JsonSerializer.SerializeAsync(stream, configuration, GetSerializerOptions(), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static JsonSerializerOptions GetSerializerOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
}
