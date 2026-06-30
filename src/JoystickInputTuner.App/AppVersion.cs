namespace JoystickInputTuner.App;



/// <summary>

/// at747 versioning — keep in sync with csproj InformationalVersion.

/// Engine (dev): VersionChannel = DEV → e.g. 2.1.3 Build DEV3P1

/// GITHUB local (pre-release): VersionChannel = PR-R → e.g. 2.1.3 Build PR-R3P1

/// </summary>

public static class AppVersion

{

    public const string ReleaseBase = "2.1.3";



    /// <summary>DEV in Engine; set PR-R in Desktop\GITHUB local mirror after robocopy.</summary>

    public const string VersionChannel = "DEV";



    public const int CycleBuildNumber = 3;



    /// <summary>Program / code changes (P).</summary>

    public const string ChangeLetters = "P";



    public const int SubNumber = 1;



    public static string BuildToken => VersionChannel == "PR-R"

        ? $"PR-R{CycleBuildNumber}{ChangeLetters}{SubNumber}"

        : $"DEV{CycleBuildNumber}{ChangeLetters}{SubNumber}";



    public static string Display => $"{ReleaseBase} Build {BuildToken}";



    public static Version AssemblyVersion => new(2, 1, 3, 0);

}


