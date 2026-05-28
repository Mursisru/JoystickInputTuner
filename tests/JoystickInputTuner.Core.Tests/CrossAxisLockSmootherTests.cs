using JoystickInputTuner.Core.Filters;
using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.Core.Tests;

public class CrossAxisLockSmootherTests
{
    [Fact]
    public void Release_DoesNotJumpInstantly()
    {
        var smoother = new CrossAxisLockSmoother();
        var shield = new CrossAxisShieldSettings
        {
            HardLockWhenActive = true,
            HardLockForceCenter = true,
            LockSmoothingSeconds = 0.05,
            ReleaseSmoothingSeconds = 0.04
        };

        smoother.Reset(0.0);
        for (var i = 0; i < 20; i++)
            smoother.Process(shield, 1.0, 0.0, 0.0, 0.005);

        var released = smoother.Process(shield, 0.0, 0.5, 0.0, 0.005);
        Assert.InRange(released, 0.0, 0.35);
    }
}
