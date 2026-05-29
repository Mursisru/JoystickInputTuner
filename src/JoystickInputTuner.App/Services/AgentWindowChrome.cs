using System.Windows;

namespace JoystickInputTuner.App.Services;

/// <summary>
/// Keeps the agent process window off-screen and out of the taskbar before first paint.
/// </summary>
public static class AgentWindowChrome
{
    public static void ApplyBeforeShow(Window window)
    {
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.WindowState = WindowState.Minimized;
        window.Visibility = Visibility.Hidden;
    }
}
