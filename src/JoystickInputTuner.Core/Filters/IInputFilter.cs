namespace JoystickInputTuner.Core.Filters;

public interface IInputFilter
{
    double Process(double value, FilterContext context);

    void Reset(double currentValue);
}
