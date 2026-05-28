using System.Text.Json;

namespace JoystickInputTuner.Core.Models;

public static class FilterSettingsSnapshot
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    public static FilterSettings Clone(FilterSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var json = JsonSerializer.Serialize(source, SerializerOptions);
        return JsonSerializer.Deserialize<FilterSettings>(json, SerializerOptions)
               ?? FilterSettingsNormalizer.Ensure(null);
    }
}
