using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace JoystickInputTuner.App.Services;

internal sealed class MonitorChartAxes
{
    public const double AxisStrokeOpacity = 0.42;

    public static readonly string[] AxisOrder = ["X", "Y", "Z", "RX", "RY", "RZ", "SL0", "SL1"];

    private static readonly Dictionary<string, string> AxisColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["X"] = "#3B82F6",
        ["Y"] = "#F59E0B",
        ["Z"] = "#A855F7",
        ["RX"] = "#06B6D4",
        ["RY"] = "#EC4899",
        ["RZ"] = "#EAB308",
        ["SL0"] = "#84CC16",
        ["SL1"] = "#F97316",
    };

    private readonly Dictionary<string, HistoryRingBuffer> _history = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Polyline> _polylines = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double[]> _scratch = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _axisEnabled = new(StringComparer.OrdinalIgnoreCase);

    public MonitorChartAxes(int capacity)
    {
        foreach (var axisId in AxisOrder)
        {
            _history[axisId] = new HistoryRingBuffer(capacity);
            _scratch[axisId] = new double[capacity];
            _axisEnabled[axisId] = true;
        }
    }

    public bool IsBound => _polylines.Count == AxisOrder.Length;

    public static string GetAxisColor(string axisId) => AxisColors[axisId.ToUpperInvariant()];

    public bool IsAxisEnabled(string axisId) =>
        _axisEnabled.TryGetValue(axisId, out var enabled) && enabled;

    public void SetAxisEnabled(string axisId, bool enabled)
    {
        _axisEnabled[axisId.ToUpperInvariant()] = enabled;
    }

    public IReadOnlyList<string> GetVisibleOverlayAxisIds(string? selectedAxisId)
    {
        var visible = new List<string>(AxisOrder.Length);
        foreach (var axisId in AxisOrder)
        {
            if (!string.IsNullOrWhiteSpace(selectedAxisId) &&
                axisId.Equals(selectedAxisId, StringComparison.OrdinalIgnoreCase))
                continue;

            if (IsAxisEnabled(axisId))
                visible.Add(axisId);
        }

        return visible;
    }

    public static string FormatAxisIdList(IReadOnlyList<string> axisIds) =>
        axisIds.Count == 0 ? "-" : string.Join(",", axisIds);

    public void BindPolyline(string axisId, Polyline polyline)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(axisId);
        ArgumentNullException.ThrowIfNull(polyline);

        var id = axisId.ToUpperInvariant();
        polyline.Opacity = AxisStrokeOpacity;
        polyline.Stroke = CreateBrush(AxisColors[id]);
        _polylines[id] = polyline;
    }

    public void RecordSample(string? selectedAxisId, IReadOnlyDictionary<string, double>? allAxes)
    {
        if (allAxes == null || allAxes.Count == 0)
            return;

        foreach (var axisId in AxisOrder)
        {
            if (!allAxes.TryGetValue(axisId, out var value))
                continue;

            var hide = !string.IsNullOrWhiteSpace(selectedAxisId) &&
                       axisId.Equals(selectedAxisId, StringComparison.OrdinalIgnoreCase);
            if (hide)
                continue;

            _history[axisId].Add(value);
        }
    }

    public void Render(double width, double height, string? selectedAxisId)
    {
        if (width <= 10 || height <= 10)
            return;

        foreach (var axisId in AxisOrder)
        {
            var hideAsSelected = !string.IsNullOrWhiteSpace(selectedAxisId) &&
                                 axisId.Equals(selectedAxisId, StringComparison.OrdinalIgnoreCase);
            var show = !hideAsSelected && IsAxisEnabled(axisId);

            if (!_polylines.TryGetValue(axisId, out var polyline))
                continue;

            polyline.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show)
                continue;

            var count = _history[axisId].CopyOrdered(_scratch[axisId]);
            polyline.Points = BuildPoints(_scratch[axisId], count, width, height);
        }
    }

    public int GetHistoryCount(string axisId) =>
        _history.TryGetValue(axisId, out var buffer) ? buffer.Count : 0;

    public void Clear()
    {
        foreach (var buffer in _history.Values)
            buffer.Clear();
    }

    private static Brush CreateBrush(string colorHex) =>
        new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)!);

    private static PointCollection BuildPoints(double[] values, int count, double width, double height)
    {
        var points = new PointCollection();
        if (count <= 0)
            return points;

        var step = count <= 1 ? 0.0 : width / (count - 1);
        for (var i = 0; i < count; i++)
        {
            var x = i * step;
            var y = ((1.0 - values[i]) * 0.5) * height;
            points.Add(new Point(x, y));
        }

        return points;
    }
}
