using System.IO;
using Microsoft.Win32;

namespace JoystickInputTuner.App.Services;

public sealed class StartupRegistrationService
{
    private const string ShortcutName = "JoystickInputTuner Agent.lnk";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "JoystickInputTuner Agent";

    public string GetStartupShortcutPath()
    {
        var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        return Path.Combine(startupFolder, ShortcutName);
    }

    public void SetEnabled(bool enabled, string executablePath, string arguments)
    {
        var shortcutPath = GetStartupShortcutPath();
        if (!enabled)
        {
            if (File.Exists(shortcutPath))
                File.Delete(shortcutPath);
            RemoveRegistryRunEntry();
            return;
        }

        if (string.IsNullOrWhiteSpace(executablePath))
            throw new InvalidOperationException("Executable path is empty.");

        SetRegistryRunEntry(executablePath, arguments);
        TryCreateStartupShortcut(shortcutPath, executablePath, arguments);
    }

    private static void SetRegistryRunEntry(string executablePath, string arguments)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Cannot access startup registry key.");
        var value = $"\"{executablePath}\" {arguments}".Trim();
        key.SetValue(RunValueName, value, RegistryValueKind.String);
    }

    private static void RemoveRegistryRunEntry()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    private static void TryCreateStartupShortcut(string shortcutPath, string executablePath, string arguments)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                return;

            var shell = Activator.CreateInstance(shellType);
            if (shell == null)
                return;

            var createShortcut = shellType.GetMethod("CreateShortcut");
            if (createShortcut == null)
                return;

            var shortcut = createShortcut.Invoke(shell, [shortcutPath]);
            if (shortcut == null)
                return;

            var shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, [executablePath]);
            shortcutType.InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, shortcut, [arguments]);
            shortcutType.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, [Path.GetDirectoryName(executablePath) ?? string.Empty]);
            shortcutType.InvokeMember("WindowStyle", System.Reflection.BindingFlags.SetProperty, null, shortcut, [7]);
            shortcutType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
        }
        catch
        {
            // Non-fatal: registry startup entry is enough for agent autostart.
        }
    }
}
