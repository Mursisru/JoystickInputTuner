using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Filters;

public static class AxisBindLockEvaluator
{
    public static bool HasBindAssignment(AxisBindLockSettings settings)
    {
        if (string.Equals(settings.BindDeviceKind, "Keyboard", StringComparison.OrdinalIgnoreCase))
            return settings.KeyCode >= 0;

        return settings.ButtonIndex >= 0 &&
               !string.IsNullOrWhiteSpace(settings.BindDeviceId);
    }

    public static bool IsBound(AxisBindLockSettings settings) =>
        settings.Enabled && HasBindAssignment(settings);

    public static bool UpdateLockActive(
        AxisBindLockSettings settings,
        bool bindPressed,
        ref bool toggleLatched,
        ref bool previousPressed)
    {
        if (!IsBound(settings))
        {
            previousPressed = false;
            toggleLatched = false;
            return false;
        }

        if (bindPressed && !previousPressed)
            toggleLatched = !toggleLatched;

        previousPressed = bindPressed;
        return toggleLatched;
    }

    public static double ApplyLock(double filteredValue, bool locked, double anchor) =>
        locked ? anchor : filteredValue;
}
