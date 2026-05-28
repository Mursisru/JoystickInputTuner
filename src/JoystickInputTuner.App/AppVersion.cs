namespace JoystickInputTuner.App;

/// <summary>
/// at747 versioning — keep in sync with csproj InformationalVersion.
/// Engine (dev): VersionChannel = DEV → e.g. 1.0.0 Build DEV1P10
/// GITHUB local (pre-release): VersionChannel = PR-R → e.g. 1.0.0 Build PR-R1P10
/// </summary>
public static class AppVersion
{
    public const string ReleaseBase = "1.0.0";

    /// <summary>PR-R in GITHUB local mirror; Engine keeps DEV.</summary>
    public const string VersionChannel = "PR-R";

    public const int CycleBuildNumber = 2;

    /// <summary>Program / code changes (P).</summary>
    public const string ChangeLetters = "P";

    public const int SubNumber = 34;

    public static string BuildToken => VersionChannel == "PR-R"
        ? $"PR-R{CycleBuildNumber}{ChangeLetters}{SubNumber}"
        : $"DEV{CycleBuildNumber}{ChangeLetters}{SubNumber}";

    public static string Display => $"{ReleaseBase} Build {BuildToken}";

    public static Version AssemblyVersion => new(1, 0, 0, 0);
}
