using System.Text.Json;
using MpcLyrics.Core;

namespace MpcLyrics.Services;

public sealed class SettingsStore
{
    private readonly string _directory;
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };
    private readonly object _sync = new();

    public SettingsStore()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "mpc-lyrics");
        _path = Path.Combine(_directory, "settings.json");
    }

    public string SettingsPath => _path;

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return AppSettings.Default();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), _options)
                           ?? AppSettings.Default();
            settings.Normalize();
            return settings;
        }
        catch (Exception error)
        {
            AppLogger.Log($"Unable to load settings; defaults will be used: {error}");
            return AppSettings.Default();
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_sync)
        {
            try
            {
                settings.Normalize();
                Directory.CreateDirectory(_directory);
                var temporary = _path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(settings, _options));
                File.Move(temporary, _path, overwrite: true);
            }
            catch (Exception error)
            {
                AppLogger.Log($"Unable to save settings: {error}");
            }
        }
    }
}
