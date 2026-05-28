using System.Globalization;
using System.IO;
using System.Text;
using JoystickInputTuner.Core.Models;

namespace JoystickInputTuner.App.Services;

public sealed class TunerDiagnosticsLogger
{
    private readonly object _sync = new();
    private readonly string _logFilePath;
    private string _lastSettingsSnapshot = string.Empty;
    private string _lastMonitorSnapshot = string.Empty;
    private DateTime _lastSettingsWriteUtc = DateTime.MinValue;
    private DateTime _lastMonitorWriteUtc = DateTime.MinValue;
    private double _lastLoggedNatural;
    private double _lastLoggedFiltered;

    public TunerDiagnosticsLogger()
    {
        var root = AppDataPaths.LogsDirectory;
        _logFilePath = Path.Combine(root, $"tuner_{DateTime.Now:yyyyMMdd}.log");
    }

    public string LogFilePath => _logFilePath;

    public bool Enabled { get; set; }

    public void ResetLogFile(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        lock (_sync)
        {
            _lastSettingsSnapshot = string.Empty;
            _lastMonitorSnapshot = string.Empty;
            _lastSettingsWriteUtc = DateTime.MinValue;
            _lastMonitorWriteUtc = DateTime.MinValue;
            _lastLoggedNatural = 0;
            _lastLoggedFiltered = 0;

            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var header = FormattableString.Invariant(
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] event=log-reset; reason={reason}; file={Path.GetFileName(_logFilePath)}");
            File.WriteAllText(_logFilePath, header + Environment.NewLine, Encoding.UTF8);
        }
    }

    public void LogAppEvent(string eventName, string message)
    {
        if (!Enabled)
            return;
        WriteLine($"event={eventName}; {message}");
    }

    public void LogSettings(
        FilterSettings settings,
        int pollingHz,
        string language,
        string deviceId,
        string axisId,
        string profileName)
    {
        if (!Enabled)
            return;

        var snapshot = FilterSettingsLogFormatter.Format(settings, pollingHz, language, deviceId, axisId, profileName);
        var now = DateTime.UtcNow;

        lock (_sync)
        {
            if (snapshot == _lastSettingsSnapshot &&
                (now - _lastSettingsWriteUtc) < TimeSpan.FromMilliseconds(250))
            {
                return;
            }

            _lastSettingsSnapshot = snapshot;
            _lastSettingsWriteUtc = now;
            WriteLineNoLock($"event=settings; {snapshot}");
        }
    }

    public void LogMovement(
        long sequence,
        double natural,
        double filtered,
        double blockStrength,
        bool otherAxesActive,
        bool targetIntent,
        double otherAxesPeak,
        IReadOnlyDictionary<string, double>? allAxes,
        int spikeSuppressedDelta,
        int hampelOutlierDelta,
        string? chartStreamAxisId = null,
        IReadOnlyList<string>? chartOverlayAxisIds = null)
    {
        if (!Enabled)
            return;

        var delta = filtered - natural;
        var naturalMoved = Math.Abs(natural - _lastLoggedNatural) >= 0.008;
        var filteredMoved = Math.Abs(filtered - _lastLoggedFiltered) >= 0.008;
        var shouldLog = sequence % 8 == 0 ||
                        naturalMoved ||
                        filteredMoved ||
                        blockStrength > 0.01 ||
                        spikeSuppressedDelta > 0 ||
                        hampelOutlierDelta > 0;

        if (!shouldLog)
            return;

        _lastLoggedNatural = natural;
        _lastLoggedFiltered = filtered;

        var axesPart = FormatAxes(allAxes);
        var chartPart = FormatChartOverlay(allAxes, chartStreamAxisId, chartOverlayAxisIds);
        var payload =
            FormattableString.Invariant(
                $"event=movement; seq={sequence}; natural={natural:0.0000}; filtered={filtered:0.0000}; correction={delta:0.0000}; block={blockStrength:0.000}; xyActive={(otherAxesActive ? 1 : 0)}; xyPeak={otherAxesPeak:0.0000}; intent={(targetIntent ? 1 : 0)}; spike+={spikeSuppressedDelta}; hampel+={hampelOutlierDelta}") +
            axesPart +
            chartPart;

        WriteLine(payload);
    }

    public void LogSample(
        double raw,
        double filtered,
        long sequence,
        int spikeSuppressedDelta,
        int hampelOutlierDelta)
    {
        LogMovement(
            sequence,
            raw,
            filtered,
            0.0,
            false,
            false,
            0.0,
            null,
            spikeSuppressedDelta,
            hampelOutlierDelta);
    }

    public void LogMonitorSnapshot(
        double raw,
        double filtered,
        int spikeSuppressedCount,
        int hampelOutlierCount,
        long sequence,
        string? chartStreamAxisId = null,
        IReadOnlyList<string>? chartOverlayAxisIds = null,
        IReadOnlyDictionary<string, double>? allAxes = null)
    {
        if (!Enabled)
            return;

        var chartPart = FormatChartOverlay(allAxes, chartStreamAxisId, chartOverlayAxisIds);

        var now = DateTime.UtcNow;
        lock (_sync)
        {
            var snapshot = FormattableString.Invariant(
                $"natural={raw:0.0000};filtered={filtered:0.0000};correction={(filtered - raw):0.0000};spikes={spikeSuppressedCount};hampel={hampelOutlierCount};sample={sequence}") +
                chartPart;
            if (snapshot == _lastMonitorSnapshot)
                return;

            if ((now - _lastMonitorWriteUtc) < TimeSpan.FromMilliseconds(200))
                return;

            _lastMonitorWriteUtc = now;
            _lastMonitorSnapshot = snapshot;
            var line = FormattableString.Invariant(
                $"event=monitor; natural={raw:0.0000}; filtered={filtered:0.0000}; correction={(filtered - raw):0.0000}; spikes={spikeSuppressedCount}; hampel={hampelOutlierCount}; sample={sequence}") +
                chartPart;
            WriteLineNoLock(line);
        }
    }

    private static string FormatAxes(IReadOnlyDictionary<string, double>? allAxes)
    {
        if (allAxes == null || allAxes.Count == 0)
            return string.Empty;

        var sb = new StringBuilder(80);
        AppendAxis(sb, allAxes, "X");
        AppendAxis(sb, allAxes, "Y");
        AppendAxis(sb, allAxes, "Z");
        AppendAxis(sb, allAxes, "RX");
        AppendAxis(sb, allAxes, "RY");
        AppendAxis(sb, allAxes, "RZ");
        return sb.ToString();
    }

    private static void AppendAxis(StringBuilder sb, IReadOnlyDictionary<string, double> axes, string id) =>
        AppendAxis(sb, axes, id, string.Empty);

    private static string FormatChartOverlay(
        IReadOnlyDictionary<string, double>? allAxes,
        string? chartStreamAxisId,
        IReadOnlyList<string>? chartOverlayAxisIds)
    {
        if (string.IsNullOrWhiteSpace(chartStreamAxisId))
            return string.Empty;

        var sb = new StringBuilder(96);
        sb.Append(CultureInfo.InvariantCulture, $"; chartStream={chartStreamAxisId}");
        sb.Append(CultureInfo.InvariantCulture, $"; chartOverlay={MonitorChartAxes.FormatAxisIdList(chartOverlayAxisIds ?? [])}");

        if (allAxes == null || chartOverlayAxisIds == null || chartOverlayAxisIds.Count == 0)
            return sb.ToString();

        foreach (var axisId in chartOverlayAxisIds)
            AppendAxis(sb, allAxes, axisId, "chart_");

        return sb.ToString();
    }

    private static void AppendAxis(StringBuilder sb, IReadOnlyDictionary<string, double> axes, string id, string prefix)
    {
        if (axes.TryGetValue(id, out var value))
            sb.Append(CultureInfo.InvariantCulture, $"; {prefix}{id.ToLowerInvariant()}={value:0.0000}");
    }

    private void WriteLine(string content)
    {
        lock (_sync)
        {
            WriteLineNoLock(content);
        }
    }

    private void WriteLineNoLock(string content)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {content}";
        File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
    }
}
