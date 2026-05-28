using System.Configuration;
using System.Data;
using System.Windows;

namespace JoystickInputTuner.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static bool IsAgentMode { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        IsAgentMode = e.Args.Any(arg => string.Equals(arg, "--agent", StringComparison.OrdinalIgnoreCase));
        base.OnStartup(e);
    }
}

