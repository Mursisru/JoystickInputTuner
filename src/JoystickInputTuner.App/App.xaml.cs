using System.Windows;
using JoystickInputTuner.App.Services;

namespace JoystickInputTuner.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static bool IsAgentMode { get; private set; }

    public static int AgentStartupDelayMs { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        IsAgentMode = e.Args.Any(arg => string.Equals(arg, "--agent", StringComparison.OrdinalIgnoreCase));
        AgentStartupDelayMs = AgentLauncher.ParseStartupDelayMs(e.Args);
        if (IsAgentMode)
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

        base.OnStartup(e);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        if (IsAgentMode)
            AgentWindowChrome.ApplyBeforeShow(mainWindow);
        mainWindow.Show();
    }
}

