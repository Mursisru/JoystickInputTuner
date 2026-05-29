using System.Diagnostics;
using System.IO;

namespace JoystickInputTuner.App.Services;

public static class AgentLauncher
{
    public const string AgentArgument = "--agent";

    public static string GetHostExecutablePath()
        => Environment.ProcessPath
           ?? Path.Combine(AppContext.BaseDirectory, "JoystickInputTuner.App.exe");

    public static void LaunchDetached(int delayMs = 0)
    {
        var executablePath = GetHostExecutablePath();
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("Host executable not found.", executablePath);

        var workingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory;
        var arguments = delayMs > 0
            ? $"{AgentArgument} --delay-ms={delayMs}"
            : AgentArgument;

        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
        });
    }

    public static int ParseStartupDelayMs(string[] args)
    {
        foreach (var arg in args)
        {
            const string prefix = "--delay-ms=";
            if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (int.TryParse(arg[prefix.Length..], out var delay) && delay >= 0)
                return Math.Min(delay, 60_000);
        }

        return 0;
    }
}
