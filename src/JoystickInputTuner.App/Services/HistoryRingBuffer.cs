namespace JoystickInputTuner.App.Services;

internal sealed class HistoryRingBuffer
{
    private readonly double[] _buffer;
    private int _count;
    private int _start;

    public HistoryRingBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _buffer = new double[capacity];
    }

    public int Count => _count;

    public void Add(double value)
    {
        if (_count < _buffer.Length)
        {
            _buffer[(_start + _count) % _buffer.Length] = value;
            _count++;
            return;
        }

        _buffer[_start] = value;
        _start = (_start + 1) % _buffer.Length;
    }

    public int CopyOrdered(Span<double> destination)
    {
        var count = Math.Min(_count, destination.Length);
        for (var i = 0; i < count; i++)
        {
            var index = _count < _buffer.Length
                ? i
                : (_start + i) % _buffer.Length;
            destination[i] = _buffer[index];
        }

        return count;
    }

    public void Clear()
    {
        _count = 0;
        _start = 0;
    }
}
