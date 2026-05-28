using System.IO;

namespace JoystickInputTuner.App.Services;

public static class AppDataPaths
{
    private const string PortableDataFolderName = "_Data";
    private static readonly string LegacyRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JoystickInputTuner");

    static AppDataPaths()
    {
        BaseDirectory = ResolveWritableDataDirectory();
        ProfilesDirectory = EnsureDirectory(Path.Combine(BaseDirectory, "profiles"));
        LogsDirectory = EnsureDirectory(Path.Combine(BaseDirectory, "logs"));
        SettingsFilePath = Path.Combine(BaseDirectory, "appsettings.json");
        FiltersFilePath = Path.Combine(BaseDirectory, "filters.json");

        if (IsPortableDistribution)
            PortableDefaultsSeeder.SeedIfMissing(BaseDirectory);
        else
            TryMigrateLegacyData();
    }

    public static bool IsPortableDistribution { get; } = DetectPortableDistribution();

    private static bool DetectPortableDistribution()
    {
#if PORTABLE_DISTRIBUTION
        return true;
#else
        var folderName = Path.GetFileName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.Equals(folderName, "Portable", StringComparison.OrdinalIgnoreCase);
#endif
    }

    public static string BaseDirectory { get; }

    public static string ProfilesDirectory { get; }

    public static string LogsDirectory { get; }

    public static string SettingsFilePath { get; }

    public static string FiltersFilePath { get; }

    private static string EnsureDirectory(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    private static string ResolveWritableDataDirectory()
    {
        var preferred = Path.Combine(AppContext.BaseDirectory, PortableDataFolderName);
        if (TryEnsureWritableDirectory(preferred))
            return preferred;

        var fallback = Path.Combine(LegacyRoot, PortableDataFolderName);
        if (TryEnsureWritableDirectory(fallback))
            return fallback;

        return EnsureDirectory(Path.Combine(Path.GetTempPath(), "JoystickInputTuner", PortableDataFolderName));
    }

    private static bool TryEnsureWritableDirectory(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
            var probe = Path.Combine(directoryPath, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryMigrateLegacyData()
    {
        try
        {
            MigrateDirectory("profiles", ProfilesDirectory);
            MigrateDirectory("logs", LogsDirectory);
            MigrateFile("appsettings.json", SettingsFilePath);
        }
        catch
        {
            // Non-fatal: app can still continue with the new _Data folder layout.
        }
    }

    private static void MigrateDirectory(string legacySubDirectory, string targetDirectory)
    {
        var sourceDirectory = Path.Combine(LegacyRoot, legacySubDirectory);
        if (!Directory.Exists(sourceDirectory))
            return;

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var destinationFile = Path.Combine(targetDirectory, relativePath);
            var destinationParent = Path.GetDirectoryName(destinationFile);
            if (!string.IsNullOrWhiteSpace(destinationParent))
                Directory.CreateDirectory(destinationParent);

            if (!File.Exists(destinationFile))
                File.Copy(sourceFile, destinationFile);
        }
    }

    private static void MigrateFile(string legacyFileName, string targetFilePath)
    {
        if (File.Exists(targetFilePath))
            return;

        var sourceFilePath = Path.Combine(LegacyRoot, legacyFileName);
        if (!File.Exists(sourceFilePath))
            return;

        File.Copy(sourceFilePath, targetFilePath);
    }
}
