using System.IO;
using System.Reflection;

namespace JoystickInputTuner.App.Services;

public static class PortableDefaultsSeeder
{
    private const string AppSettingsResource = "JoystickInputTuner.App.Defaults.portable-appsettings.json";
    private const string DefaultProfileResource = "JoystickInputTuner.App.Defaults.default-profile.json";
    private const string Ta320ProfileResource = "JoystickInputTuner.App.Defaults.ta320-pilot-profile.json";

    public static void SeedIfMissing(string dataRoot)
    {
        var settingsPath = Path.Combine(dataRoot, "appsettings.json");
        var profilesDir = Path.Combine(dataRoot, "profiles");
        var defaultProfilePath = Path.Combine(profilesDir, "Default.json");
        var ta320ProfilePath = Path.Combine(profilesDir, "T.A320 Pilot.json");
        var logsDir = Path.Combine(dataRoot, "logs");

        Directory.CreateDirectory(profilesDir);
        Directory.CreateDirectory(logsDir);

        if (!File.Exists(settingsPath))
            WriteEmbeddedResource(AppSettingsResource, settingsPath);

        if (!File.Exists(defaultProfilePath))
            WriteEmbeddedResource(DefaultProfileResource, defaultProfilePath);

        if (!File.Exists(ta320ProfilePath))
            WriteEmbeddedResource(Ta320ProfileResource, ta320ProfilePath);
    }

    public static void ResetPortableData(string portableOutputDir)
    {
        var dataRoot = Path.Combine(portableOutputDir, "_Data");
        if (Directory.Exists(dataRoot))
            Directory.Delete(dataRoot, recursive: true);

        Directory.CreateDirectory(dataRoot);
        WriteEmbeddedResource(AppSettingsResource, Path.Combine(dataRoot, "appsettings.json"));
        var profilesDir = Path.Combine(dataRoot, "profiles");
        Directory.CreateDirectory(profilesDir);
        Directory.CreateDirectory(Path.Combine(dataRoot, "logs"));
        WriteEmbeddedResource(DefaultProfileResource, Path.Combine(profilesDir, "Default.json"));
        WriteEmbeddedResource(Ta320ProfileResource, Path.Combine(profilesDir, "T.A320 Pilot.json"));
    }

    private static void WriteEmbeddedResource(string resourceName, string destinationPath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource: {resourceName}");
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(destinationPath, content);
    }
}
