using System.IO;
using System.Text.Json;
using JoystickInputTuner.App.Models;
using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.App.Services;

public sealed class FilterSessionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string FilePath => AppDataPaths.FiltersFilePath;

    public async Task SaveAsync(FilterSessionState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.SchemaVersion = FilterSessionState.CurrentSchemaVersion;
        state.SavedAt = DateTimeOffset.UtcNow;
        state.Filters = FilterSettingsNormalizer.Ensure(state.Filters);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(FilePath);
        await JsonSerializer.SerializeAsync(stream, state, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FilterSessionState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
            return null;

        await using var stream = File.OpenRead(FilePath);
        var state = await JsonSerializer.DeserializeAsync<FilterSessionState>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        if (state?.Filters == null)
            return null;

        state.Filters = FilterSettingsNormalizer.Ensure(state.Filters);
        state.Calibration ??= new CalibrationSettings();

        // Legacy files (before UserModified) were always written after user tweaks.
        if (!state.UserModified)
            state.UserModified = true;

        state.MonitorChartEnabledAxes ??= [];
        state.Ui ??= new SessionUiPreferences();

        if (state.SchemaVersion < 1)
            state.SchemaVersion = 1;

        return state;
    }
}
