namespace JoystickInputTuner.Core.Models;

public sealed class CalibrationSettings
{
    public double Min { get; set; } = -1.0;

    public double Center { get; set; } = 0.0;

    public double Max { get; set; } = 1.0;

    public double Normalize(double rawValue)
    {
        if (rawValue >= Center)
        {
            var topRange = Math.Max(0.0001, Max - Center);
            return Math.Clamp((rawValue - Center) / topRange, -1.0, 1.0);
        }

        var bottomRange = Math.Max(0.0001, Center - Min);
        return Math.Clamp((rawValue - Center) / bottomRange, -1.0, 1.0);
    }
}
