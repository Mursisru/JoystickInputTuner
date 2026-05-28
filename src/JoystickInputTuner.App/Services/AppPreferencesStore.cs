using System.Text.Json;
using System.IO;
using JoystickInputTuner.App.Models;

namespace JoystickInputTuner.App.Services;

public sealed class AppPreferencesStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public AppPreferencesStore()
    {
        _settingsPath = AppDataPaths.SettingsFilePath;
    }

    public string SettingsPath => _settingsPath;

    public async Task<AppPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
            return new AppPreferences();

        await using var stream = File.OpenRead(_settingsPath);
        var prefs = await JsonSerializer.DeserializeAsync<AppPreferences>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        return prefs ?? new AppPreferences();
    }

    public async Task SaveAsync(AppPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, preferences, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }
}
