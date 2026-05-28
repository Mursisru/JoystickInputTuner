using System.Text.Json;
using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Services;

public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public async Task SaveAsync(string filePath, TunerProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, profile, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<TunerProfile> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        var profile = await JsonSerializer.DeserializeAsync<TunerProfile>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        profile ??= new TunerProfile();
        profile.Filters = FilterSettingsNormalizer.Ensure(profile.Filters);
        profile.Calibration ??= new CalibrationSettings();
        return profile;
    }
}
