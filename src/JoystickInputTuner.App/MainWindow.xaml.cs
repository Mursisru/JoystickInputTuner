using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using JoystickInputTuner.App.Models;
using JoystickInputTuner.App.Services;
using JoystickInputTuner.App.Views;
using JoystickInputTuner.Core.Filters;
using JoystickInputTuner.Core.Models;
using JoystickInputTuner.Core.Output;
using JoystickInputTuner.Core.Services;
using JoystickInputTuner.Input;
using JoystickInputTuner.Input.Output;
using JoystickInputTuner.Input.Models;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace JoystickInputTuner.App;

public partial class MainWindow : Window
{
    private const int HistoryLength = 360;

    private readonly IJoystickInputProvider _inputProvider;
    private readonly FilterPipeline _pipeline;
    private readonly ProfileStore _profileStore;
    private readonly AppPreferencesStore _preferencesStore;
    private readonly StartupRegistrationService _startupService;
    private readonly ObservableCollection<InputDeviceInfo> _devices = [];
    private readonly ObservableCollection<InputAxisInfo> _axes = [];
    private readonly VJoyOutputSink _vJoySink = new();
    private readonly Dictionary<string, IOutputSink> _outputSinks;
    private readonly DispatcherTimer _uiTimer;
    private readonly Dictionary<string, Dictionary<string, string>> _text = new(StringComparer.OrdinalIgnoreCase);
    private readonly TunerDiagnosticsLogger _diagnostics = new();
    private readonly FilterSessionStore _filterSessionStore = new();
    private readonly DispatcherTimer _filterSaveTimer;
    private bool _suppressFilterSessionSave;
    private bool _filterSessionDirty;

    private readonly object _historySync = new();
    private readonly HistoryRingBuffer _rawHistory = new(HistoryLength);
    private readonly HistoryRingBuffer _filteredHistory = new(HistoryLength);
    private readonly MonitorChartAxes _monitorChartAxes = new(HistoryLength);
    private readonly Dictionary<string, CheckBox> _monitorAxisToggles = new(StringComparer.OrdinalIgnoreCase);
    private bool _monitorAxisTogglesBuilt;
    private bool _suppressMonitorAxisToggle;
    private readonly double[] _rawChartScratch = new double[HistoryLength];
    private readonly double[] _filteredChartScratch = new double[HistoryLength];
    private volatile bool _monitorUiEnabled;

    private double _latestRaw;
    private double _latestFiltered;
    private long _latestSequence;
    private Dictionary<string, double> _latestAllAxes = new(StringComparer.OrdinalIgnoreCase);
    private bool _isRunning;
    private string _streamAxisId = string.Empty;
    private IOutputSink? _activeOutputSink;
    private string _language = "RU";
    private TunerProfile _profile = new();
    private AppPreferences _preferences = new();
    private int _lastLoggedSpikeCount;
    private int _lastLoggedHampelCount;
    private int _saturatedRawSamples;
    private bool _saturationWarned;
    private bool _updatingUiFromPreferences;
    private readonly string _profilesRoot;
    private Dictionary<string, double>? _lastAxesSnapshot;
    private DateTimeOffset _lastAxesSnapshotTimestamp;
    private readonly AxisIntentTracker _axisIntentTracker = new();
    private readonly CrossAxisLockSmoother _crossAxisLockSmoother = new();
    private bool _crossAxisActivityLatched;
    private int _otherAxisSustainedSamples;
    private DateTimeOffset _lastPollTimestamp;
    private int _streamPollingHz = 175;
    private bool _axisBindLockToggleLatched;
    private bool _axisBindLockPreviousPressed;
    private bool _axisBindLockActive;
    private bool _closeAfterHandoff;
    private bool _handoffInProgress;
    private bool _agentSpawnedForHandoff;

    public MainWindow()
    {
        _filterSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _filterSaveTimer.Tick += async (_, _) =>
        {
            _filterSaveTimer.Stop();
            await SaveFilterSessionAsync().ConfigureAwait(true);
        };

        _suppressFilterSessionSave = true;
        InitializeComponent();
        if (App.IsAgentMode)
            AgentWindowChrome.ApplyBeforeShow(this);
        Title = $"JoystickInputTuner — {AppVersion.Display}";

        _inputProvider = new DirectInputProvider();
        _pipeline = new FilterPipeline();
        _profileStore = new ProfileStore();
        _preferencesStore = new AppPreferencesStore();
        _startupService = new StartupRegistrationService();
        _outputSinks = new Dictionary<string, IOutputSink>(StringComparer.OrdinalIgnoreCase)
        {
            ["Monitor"] = new MonitorSink(),
            ["vJoy"] = _vJoySink,
        };
        foreach (var pair in BuildLocalization())
            _text[pair.Key] = pair.Value;

        _profilesRoot = AppDataPaths.ProfilesDirectory;

        DeviceComboBox.ItemsSource = _devices;
        AxisComboBox.ItemsSource = _axes;

        OutputSinkComboBox.ItemsSource = _outputSinks.Values.ToList();
        OutputSinkComboBox.DisplayMemberPath = "Name";
        SelectOutputSink(_vJoySink.IsAvailable ? "vJoy" : "Monitor");

        _inputProvider.ReadingAvailable += InputProviderOnReadingAvailable;
        Loaded += MainWindow_OnLoaded;
        Closing += MainWindow_OnClosing;
        IsVisibleChanged += (_, _) => UpdateMonitorUiEnabled();
        MainTabControl.SelectionChanged += MainTabControl_OnSelectionChanged;

        _uiTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _uiTimer.Tick += (_, _) => RefreshMonitor();
        _uiTimer.Start();

        SyncFilterUiText();
        SyncFilterPipelineFromUi();
        // Keep _suppressFilterSessionSave=true until MainWindow_OnLoaded finishes restore.
    }

    private Dictionary<string, Dictionary<string, string>> BuildLocalization()
    {
        return new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["app.subtitle"] = new() { ["RU"] = "Диагностика и фильтрация рывков оси рыскания", ["EN"] = "Yaw-axis spike diagnostics and filtering" },
            ["profiles"] = new() { ["RU"] = "Профили", ["EN"] = "Profiles" },
            ["profile.title"] = new() { ["RU"] = "Профиль: \"{0}\"", ["EN"] = "Profile: \"{0}\"" },
            ["instruction.header"] = new() { ["RU"] = "Инструкция", ["EN"] = "Instruction" },
            ["instruction.body"] = new()
            {
                ["RU"] = "1) Обнови устройства.\n2) Выбери джойстик.\n3) Нажми \"Определить ось\" и подвигай нужную ось.\n4) Подбери фильтры и смотри график Monitor.\n5) Сохрани профиль JSON.",
                ["EN"] = "1) Refresh devices.\n2) Select joystick.\n3) Click \"Detect axis\" and move the needed axis.\n4) Tune filters and watch Monitor graph.\n5) Save profile JSON."
            },
            ["btn.refresh"] = new() { ["RU"] = "Обновить устройства", ["EN"] = "Refresh devices" },
            ["btn.apply"] = new() { ["RU"] = "Применить", ["EN"] = "Apply" },
            ["btn.start"] = new() { ["RU"] = "Старт", ["EN"] = "Start" },
            ["btn.stop"] = new() { ["RU"] = "Стоп", ["EN"] = "Stop" },
            ["btn.detect"] = new() { ["RU"] = "Определить ось", ["EN"] = "Detect axis" },
            ["btn.addProfile"] = new() { ["RU"] = "Добавить", ["EN"] = "Add" },
            ["btn.deleteProfile"] = new() { ["RU"] = "Удалить", ["EN"] = "Delete" },
            ["btn.saveProfile"] = new() { ["RU"] = "Сохранить", ["EN"] = "Save" },
            ["btn.loadProfile"] = new() { ["RU"] = "Загрузить", ["EN"] = "Load" },
            ["label.language"] = new() { ["RU"] = "Язык", ["EN"] = "Language" },
            ["hint.moveAxis"] = new() { ["RU"] = "Нажми «Определить ось» и подвигай нужную ось джойстика.", ["EN"] = "Click \"Detect axis\" and move the joystick axis you need." },
            ["tab.input"] = new() { ["RU"] = "Ввод", ["EN"] = "Input" },
            ["tab.filters"] = new() { ["RU"] = "Фильтры", ["EN"] = "Filters" },
            ["tab.monitor"] = new() { ["RU"] = "Монитор", ["EN"] = "Monitor" },
            ["tab.settings"] = new() { ["RU"] = "Настройки", ["EN"] = "Settings" },
            ["settings.header"] = new() { ["RU"] = "Настройки приложения", ["EN"] = "Application settings" },
            ["settings.logging"] = new() { ["RU"] = "Включить диагностический лог", ["EN"] = "Enable diagnostics log" },
            ["settings.resetLogOnStartup"] = new() { ["RU"] = "Очищать лог при запуске приложения", ["EN"] = "Clear log file on app start" },
            ["settings.clearLog"] = new() { ["RU"] = "Очистить лог", ["EN"] = "Clear log now" },
            ["settings.hints"] = new() { ["RU"] = "Показывать подсказки фильтров", ["EN"] = "Show filter hints" },
            ["settings.autoApply"] = new() { ["RU"] = "Автоприменять последние применённые настройки при запуске приложения", ["EN"] = "Auto-apply last applied settings when app opens" },
            ["settings.startup"] = new() { ["RU"] = "Запускать агент с Windows", ["EN"] = "Start agent with Windows" },
            ["settings.reset"] = new() { ["RU"] = "Сбросить применённые настройки", ["EN"] = "Reset applied settings" },
            ["settings.defaults"] = new() { ["RU"] = "Стандартные настройки", ["EN"] = "Default settings" },
            ["header.inputSource"] = new() { ["RU"] = "Источник ввода", ["EN"] = "Input Source" },
            ["header.calibration"] = new() { ["RU"] = "Калибровка", ["EN"] = "Calibration" },
            ["label.device"] = new() { ["RU"] = "Устройство", ["EN"] = "Device" },
            ["label.axis"] = new() { ["RU"] = "Ось", ["EN"] = "Axis" },
            ["label.polling"] = new() { ["RU"] = "Опрос (Гц)", ["EN"] = "Polling (Hz)" },
            ["label.output"] = new() { ["RU"] = "Выходной sink", ["EN"] = "Output sink" },
            ["label.profileName"] = new() { ["RU"] = "Имя профиля", ["EN"] = "Profile name" },
            ["label.min"] = new() { ["RU"] = "Минимум", ["EN"] = "Min" },
            ["label.center"] = new() { ["RU"] = "Центр", ["EN"] = "Center" },
            ["label.max"] = new() { ["RU"] = "Максимум", ["EN"] = "Max" },
            ["header.deadzone"] = new() { ["RU"] = "Мёртвая зона", ["EN"] = "Deadzone" },
            ["header.medianHampel"] = new() { ["RU"] = "Median / Hampel", ["EN"] = "Median / Hampel" },
            ["header.spike"] = new() { ["RU"] = "Анти-рывок и сглаживание", ["EN"] = "Anti-spike and smoothing" },
            ["check.enabled"] = new() { ["RU"] = "Включено", ["EN"] = "Enabled" },
            ["check.deadzoneDynamic"] = new() { ["RU"] = "Динамическая мёртвая зона", ["EN"] = "Dynamic deadzone" },
            ["check.medianEnabled"] = new() { ["RU"] = "Включить Median фильтр", ["EN"] = "Median filter enabled" },
            ["check.hampelEnabled"] = new() { ["RU"] = "Включить Hampel фильтр", ["EN"] = "Hampel outlier filter enabled" },
            ["check.spikeEnabled"] = new() { ["RU"] = "Включить anti-spike", ["EN"] = "Anti-spike gate enabled" },
            ["check.radialSpike"] = new() { ["RU"] = "Радиальные зоны (центр / край)", ["EN"] = "Radial zones (center / outer)" },
            ["label.centerZone"] = new() { ["RU"] = "Граница центральной зоны |raw|", ["EN"] = "Center zone boundary |raw|" },
            ["label.centerSpikeSensitivity"] = new() { ["RU"] = "Чувствительность у центра (delta)", ["EN"] = "Center spike sensitivity (delta)" },
            ["label.centerSmoothingMultiplier"] = new() { ["RU"] = "Множитель сглаживания в центре", ["EN"] = "Center smoothing multiplier" },
            ["header.zImpulseGuard"] = new() { ["RU"] = "Защита от ложных импульсов Z", ["EN"] = "Z impulse protection" },
            ["check.zImpulseGuardEnabled"] = new() { ["RU"] = "Защищать от ложных Z-импульсов у центра", ["EN"] = "Protect from false Z impulses near center" },
            ["label.zImpulseCenterRadius"] = new() { ["RU"] = "Радиус защиты у центра |raw|", ["EN"] = "Guard center radius |raw|" },
            ["label.zImpulseThreshold"] = new() { ["RU"] = "Порог подтверждения движения (delta)", ["EN"] = "Intent threshold (delta)" },
            ["label.zImpulseConfirm"] = new() { ["RU"] = "Сэмплов для подтверждения", ["EN"] = "Confirm samples" },
            ["hint.zImpulseGuard"] = new()
            {
                ["RU"] = "Блокирует короткие Z-рывки на 1-2 сэмпла у центра; устойчивое движение пропускается после подтверждения.",
                ["EN"] = "Blocks brief one/two-sample Z jerks near center; sustained movement passes after confirmation."
            },
            ["header.axisIntent"] = new() { ["RU"] = "Намерение (целевая ось)", ["EN"] = "Axis intent (target)" },
            ["check.axisIntentEnabled"] = new() { ["RU"] = "Определять намеренное движение выбранной оси", ["EN"] = "Detect intentional movement on selected axis" },
            ["label.axisIntentDeflection"] = new() { ["RU"] = "Порог отклонения для намерения |value|", ["EN"] = "Intent deflection threshold |value|" },
            ["label.axisIntentStrong"] = new() { ["RU"] = "Сильное отклонение (мгновенное намерение)", ["EN"] = "Strong deflection (instant intent)" },
            ["label.axisIntentConfirm"] = new() { ["RU"] = "Сэмплов для подтверждения", ["EN"] = "Confirm samples" },
            ["hint.axisIntent"] = new()
            {
                ["RU"] = "Намеренное движение отключает кросс-блокировку и Z impulse guard. Короткие импульсы считаются паразитными.",
                ["EN"] = "Intentional movement disables cross-axis lock and Z impulse guard. Brief impulses stay parasitic."
            },
            ["header.crossAxisShield"] = new() { ["RU"] = "Кросс-осевая защита", ["EN"] = "Cross-axis shield" },
            ["check.crossAxisDominance"] = new() { ["RU"] = "Другие оси должны быть сильнее целевой", ["EN"] = "Require watched axes stronger than target" },
            ["label.crossAxisDominance"] = new() { ["RU"] = "Коэффициент доминирования других осей", ["EN"] = "Other-axis dominance ratio" },
            ["check.crossAxisRespectIntent"] = new() { ["RU"] = "Отключать shield при намеренном движении цели", ["EN"] = "Disable shield while target movement is intentional" },
            ["check.crossAxisShieldEnabled"] = new() { ["RU"] = "Усиливать фильтрацию выбранной оси при активности других осей", ["EN"] = "Strengthen selected axis when other axes are active" },
            ["label.crossAxisTarget"] = new() { ["RU"] = "Целевая ось защиты", ["EN"] = "Target axis for shield" },
            ["label.crossAxisWatch"] = new() { ["RU"] = "Оси-наблюдатели (источник активности)", ["EN"] = "Watched axes (activity source)" },
            ["label.crossAxisDeflection"] = new() { ["RU"] = "Порог отклонения других осей |value|", ["EN"] = "Other axis deflection threshold |value|" },
            ["label.crossAxisVelMin"] = new() { ["RU"] = "Мин. скорость других осей / сек", ["EN"] = "Other axis velocity min / sec" },
            ["label.crossAxisVelMax"] = new() { ["RU"] = "Макс. скорость других осей / сек", ["EN"] = "Other axis velocity max / sec" },
            ["label.crossAxisRateMul"] = new() { ["RU"] = "Множитель rate limiter при активности", ["EN"] = "Rate limiter multiplier when active" },
            ["label.crossAxisEmaMul"] = new() { ["RU"] = "Множитель EMA alpha при активности", ["EN"] = "EMA alpha multiplier when active" },
            ["check.crossAxisHardLock"] = new() { ["RU"] = "Жёстко блокировать целевую ось при активности других осей", ["EN"] = "Hard lock target axis while other axes are active" },
            ["check.crossAxisHardLockCenter"] = new() { ["RU"] = "Фиксировать хардлок только в центре (0.0)", ["EN"] = "Hard lock anchor only at center (0.0)" },
            ["label.crossAxisLeakMul"] = new() { ["RU"] = "Множитель пропуска при блокировке (чуть-чуть движения)", ["EN"] = "Lock leak multiplier (tiny manual activation)" },
            ["hint.crossAxisShield"] = new()
            {
                ["RU"] = "Плавная блокировка: наблюдаемые оси держатся несколько сэмплов, сила растёт постепенно. Hard-lock тянет к центру без резких углов.",
                ["EN"] = "Smooth block: watched axes must hold for several samples; strength ramps in. Hard-lock eases to center without sharp corners."
            },
            ["hint.centerSmoothingMultiplier"] = new()
            {
                ["RU"] = "1.0 — по умолчанию; больше — сильнее режет резкие микродвижения у центра.",
                ["EN"] = "1.0 = default; higher = stronger suppression of sharp micro-moves near center."
            },
            ["label.outerSpikeDelta"] = new() { ["RU"] = "Порог рывка на краю (delta)", ["EN"] = "Outer spike delta threshold" },
            ["label.outerSpikeVelocity"] = new() { ["RU"] = "Скорость рывка на краю / сек", ["EN"] = "Outer spike velocity / sec" },
            ["hint.radialSpike"] = new()
            {
                ["RU"] = "У центра режутся резкие микродвижения; на большой амплитуде — только очень резкие скачки.",
                ["EN"] = "Near center: sharp micro-spikes are cut; at full deflection: only very sharp jumps."
            },
            ["check.rateEnabled"] = new() { ["RU"] = "Включить ограничитель скорости", ["EN"] = "Rate limiter enabled" },
            ["check.emaEnabled"] = new() { ["RU"] = "Включить EMA сглаживание", ["EN"] = "EMA smoothing enabled" },
            ["label.deadzoneRadius"] = new() { ["RU"] = "Радиус", ["EN"] = "Radius" },
            ["label.medianWindow"] = new() { ["RU"] = "Окно Median", ["EN"] = "Median window" },
            ["label.hampelWindow"] = new() { ["RU"] = "Окно Hampel", ["EN"] = "Hampel window" },
            ["label.hampelSigma"] = new() { ["RU"] = "Sigma множитель", ["EN"] = "Sigma multiplier" },
            ["label.spikeDelta"] = new() { ["RU"] = "Порог рывка (delta)", ["EN"] = "Spike delta threshold" },
            ["label.spikeVelocity"] = new() { ["RU"] = "Скорость рывка / сек", ["EN"] = "Spike velocity / sec" },
            ["label.rateLimiter"] = new() { ["RU"] = "Макс. delta / сек", ["EN"] = "Max delta / sec" },
            ["label.emaAlpha"] = new() { ["RU"] = "EMA alpha", ["EN"] = "EMA alpha" },
            ["label.spikeHold"] = new() { ["RU"] = "Макс. подавлений подряд", ["EN"] = "Max suppressed in a row" },
            ["hint.spikeHold"] = new()
            {
                ["RU"] = "Значение 1 подавляет только одиночный выброс, затем пропускает устойчивое движение.",
                ["EN"] = "Value 1 suppresses only a single spike, then allows sustained movement."
            },
            ["hint.deadzone"] = new()
            {
                ["RU"] = "Мёртвая зона убирает микродёргания возле центра при лёгком касании стика.",
                ["EN"] = "Deadzone removes tiny center jitter when you barely touch the stick."
            },
            ["hint.medianHampel"] = new()
            {
                ["RU"] = "Median/Hampel подавляют случайные выбросы, сохраняя обычные движения.",
                ["EN"] = "Median/Hampel suppress random spikes without changing normal stick travel too much."
            },
            ["hint.rateLimiter"] = new()
            {
                ["RU"] = "Ограничитель скорости режет слишком быстрые скачки и стабилизирует выход.",
                ["EN"] = "Rate limiter caps too-fast jumps and keeps output smoother."
            },
            ["hint.ema"] = new()
            {
                ["RU"] = "EMA сглаживает движение; меньший alpha — мягче, но с большей задержкой.",
                ["EN"] = "EMA smooths movement; lower alpha is softer but adds more lag."
            },
            ["monitor.axesTitle"] = new()
            {
                ["RU"] = "Другие оси на графике (сырой вход устройства)",
                ["EN"] = "Other axes on chart (device raw)"
            },
            ["monitor.axisOnOutput"] = new()
            {
                ["RU"] = "Уже отображается линиями Raw/Filtered",
                ["EN"] = "Already shown on Raw/Filtered lines"
            },
            ["monitor.legendDeviceRaw"] = new() { ["RU"] = "сырой вход устройства", ["EN"] = "device raw input" },
            ["header.axisBindLock"] = new() { ["RU"] = "Блокировка оси кнопкой", ["EN"] = "Axis lock by button" },
            ["check.axisBindLockEnabled"] = new()
            {
                ["RU"] = "Полностью блокировать выбранную ось (центр) при активном бинде",
                ["EN"] = "Fully lock selected axis at center when bind is active"
            },
            ["btn.axisBindLock"] = new() { ["RU"] = "Назначить кнопку…", ["EN"] = "Bind button…" },
            ["btn.axisBindReset"] = new() { ["RU"] = "Сброс бинда", ["EN"] = "Reset bind" },
            ["hint.axisBindLock"] = new()
            {
                ["RU"] = "Нужен запущенный Start. «Назначить» — кнопка джойстика, клавиша или кнопка мыши (5 сек). Первое нажатие бинда включает блокировку оси в центре, второе — выключает.",
                ["EN"] = "Requires Start running. Bind: joystick button, key, or mouse button (5 sec). First bind press locks the stream axis to center; second press unlocks."
            },
            ["label.axisBindNotSet"] = new() { ["RU"] = "Бинд: не назначен", ["EN"] = "Bind: not set" },
            ["label.axisBindJoystick"] = new() { ["RU"] = "Бинд: {0}, кн. {1}", ["EN"] = "Bind: {0}, btn {1}" },
            ["label.axisBindKeyboard"] = new() { ["RU"] = "Бинд: клавиатура, {0}", ["EN"] = "Bind: keyboard, {0}" },
            ["label.axisBindMouse"] = new() { ["RU"] = "Бинд: мышь, {0}", ["EN"] = "Bind: mouse, {0}" },
            ["label.axisBindLocked"] = new() { ["RU"] = " (блокировка ВКЛ)", ["EN"] = " (lock ON)" },
            ["label.axisBindUnlocked"] = new() { ["RU"] = " (блокировка выкл)", ["EN"] = " (lock off)" },
            ["status.bindAxisDoneJoystick"] = new() { ["RU"] = "Бинд: {0}, кн. {1}", ["EN"] = "Bound: {0}, btn {1}" },
            ["status.bindAxisDoneKeyboard"] = new() { ["RU"] = "Бинд: клавиша {0}", ["EN"] = "Bound: key {0}" },
            ["status.bindAxisDoneMouse"] = new() { ["RU"] = "Бинд: мышь, {0}", ["EN"] = "Bound: mouse, {0}" },
            ["status.bindAxisWaiting"] = new()
            {
                ["RU"] = "Нажми кнопку на любом контроллере (5 сек)…",
                ["EN"] = "Press a button on any controller (5 sec)…"
            },
            ["status.bindAxisDone"] = new() { ["RU"] = "Бинд: {0}, кнопка {1}", ["EN"] = "Bound: {0}, button {1}" },
            ["status.bindAxisFail"] = new()
            {
                ["RU"] = "Кнопка не назначена. Выбери устройство и нажми кнопку на джойстике.",
                ["EN"] = "No button captured. Select device and press a joystick button."
            },
            ["metric.raw"] = new() { ["RU"] = "Сырой", ["EN"] = "Raw" },
            ["metric.filtered"] = new() { ["RU"] = "Фильтрованный", ["EN"] = "Filtered" },
            ["metric.delta"] = new() { ["RU"] = "Дельта", ["EN"] = "Delta" },
            ["metric.spikes"] = new() { ["RU"] = "Подавлено рывков", ["EN"] = "Suppressed spikes" },
            ["metric.hampel"] = new() { ["RU"] = "Выбросов Hampel", ["EN"] = "Hampel outliers" },
            ["metric.sample"] = new() { ["RU"] = "Сэмпл #", ["EN"] = "Sample #" },
            ["status.ready"] = new() { ["RU"] = "Готово", ["EN"] = "Ready" },
            ["status.noDevices"] = new() { ["RU"] = "Джойстики не найдены.", ["EN"] = "No game controllers detected." },
            ["status.devices"] = new() { ["RU"] = "Устройств: {0}", ["EN"] = "Devices: {0}" },
            ["status.running"] = new() { ["RU"] = "Запущено: {0} / {1}", ["EN"] = "Running: {0} / {1}" },
            ["status.stopped"] = new() { ["RU"] = "Остановлено.", ["EN"] = "Stopped." },
            ["status.selectDevice"] = new() { ["RU"] = "Сначала выбери устройство.", ["EN"] = "Select device first." },
            ["status.selectAxis"] = new() { ["RU"] = "Сначала выбери ось.", ["EN"] = "Select axis first." },
            ["status.detectAxisStart"] = new() { ["RU"] = "Определение оси... подвигай нужную ось 2-3 секунды.", ["EN"] = "Detecting axis... move the target axis for 2-3 seconds." },
            ["status.detectAxisDone"] = new() { ["RU"] = "Найдена активная ось: {0}", ["EN"] = "Detected active axis: {0}" },
            ["status.detectAxisFail"] = new() { ["RU"] = "Не удалось определить активную ось. Подвигай сильнее и попробуй снова.", ["EN"] = "Could not detect active axis. Move stronger and retry." },
            ["status.profileSaved"] = new() { ["RU"] = "Профиль сохранён: {0}", ["EN"] = "Profile saved: {0}" },
            ["status.profileLoaded"] = new() { ["RU"] = "Профиль загружен: {0}", ["EN"] = "Profile loaded: {0}" },
            ["status.filtersRestored"] = new() { ["RU"] = "Восстановлены сохранённые настройки фильтров.", ["EN"] = "Restored saved filter settings." },
            ["status.logReset"] = new() { ["RU"] = "Лог очищен: {0}", ["EN"] = "Log cleared: {0}" },
            ["status.profileDeleted"] = new() { ["RU"] = "Профиль удалён.", ["EN"] = "Profile deleted." },
            ["status.profileDeleteBlocked"] = new() { ["RU"] = "Нельзя удалить последний профиль.", ["EN"] = "Cannot delete the last profile." },
            ["status.applied"] = new() { ["RU"] = "Настройки применены: {0}", ["EN"] = "Settings applied: {0}" },
            ["status.resetApplied"] = new() { ["RU"] = "Применённые настройки сброшены.", ["EN"] = "Applied settings reset." },
            ["status.defaultsApplied"] = new() { ["RU"] = "Стандартные настройки применены.", ["EN"] = "Default settings applied." },
            ["status.startupAgentOn"] = new() { ["RU"] = "Автозагрузка агента включена.", ["EN"] = "Startup agent enabled." },
            ["status.startupAgentOff"] = new() { ["RU"] = "Автозагрузка агента отключена.", ["EN"] = "Startup agent disabled." },
            ["status.startupAgentError"] = new() { ["RU"] = "Не удалось настроить автозагрузку: {0}", ["EN"] = "Failed to configure startup: {0}" },
            ["status.saturatedInput"] = new()
            {
                ["RU"] = "Вход зажат около {0}. Проверь выбранную ось и калибровку.",
                ["EN"] = "Input is saturated near {0}. Check selected axis and calibration."
            },
            ["status.language"] = new() { ["RU"] = "Язык интерфейса: русский", ["EN"] = "UI language: English" },
            ["status.logging"] = new() { ["RU"] = "Диагностический лог: {0}", ["EN"] = "Diagnostics log: {0}" },
            ["status.vjoyMissing"] = new()
            {
                ["RU"] = "vJoy не найден. Установи драйвер vJoy и включи устройство #1 в Configure vJoy.",
                ["EN"] = "vJoy is not available. Install vJoy driver and enable device #1 in Configure vJoy."
            },
            ["status.vjoyActive"] = new()
            {
                ["RU"] = "В vJoy (устройство #{0}) уходит только выбранная ось. В симуляторе/игре привяжи ось к этому vJoy-устройству.",
                ["EN"] = "Only the selected axis is sent to vJoy (device #{0}). Bind that axis to this vJoy device in your sim/game."
            },
            ["status.vjoyBusy"] = new()
            {
                ["RU"] = "vJoy #{0} занят. Закрой симулятор/игру и другие программы с vJoy, нажми «Стоп» в Tuner, перезапусти vJoy или смени номер устройства.",
                ["EN"] = "vJoy #{0} is busy. Close your sim/game and other vJoy apps, press Stop in Tuner, restart vJoy, or change device id."
            },
            ["status.outputUnavailable"] = new() { ["RU"] = "Выход недоступен: {0}", ["EN"] = "Output unavailable: {0}" },
        };
    }

    private string T(string key)
    {
        if (_text.Count == 0)
            return key;

        if (_text.TryGetValue(key, out var langs))
        {
            if (langs.TryGetValue(_language, out var value))
                return value;
            if (langs.TryGetValue("EN", out var fallback))
                return fallback;
        }

        return key;
    }

    private void SetStatus(string key, params object[] args)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetStatus(key, args));
            return;
        }

        var text = T(key);
        StatusTextBlock.Text = args.Length > 0 ? string.Format(text, args) : text;
    }

    private void ApplyLanguage()
    {
        if (!AreLanguageControlsReady())
            return;

        AppSubtitleTextBlock.Text = T("app.subtitle");
        if (AppVersionTextBlock != null)
            AppVersionTextBlock.Text = AppVersion.Display;
        ProfilesHeaderTextBlock.Text = T("profiles");
        LanguageLabelTextBlock.Text = T("label.language");
        InstructionHeaderTextBlock.Text = T("instruction.header");
        InstructionTextBlock.Text = T("instruction.body");

        AddProfileButton.Content = T("btn.addProfile");
        DeleteProfileButton.Content = T("btn.deleteProfile");
        RefreshDevicesButton.Content = T("btn.refresh");
        ApplySettingsButton.Content = T("btn.apply");
        StartButton.Content = T("btn.start");
        StopButton.Content = T("btn.stop");
        DetectAxisButton.Content = T("btn.detect");
        SaveProfileButton.Content = T("btn.saveProfile");
        LoadProfileButton.Content = T("btn.loadProfile");
        MovementHintTextBlock.Text = T("hint.moveAxis");
        if (AxisBindLockHeaderTextBlock != null)
            AxisBindLockHeaderTextBlock.Text = T("header.axisBindLock");
        if (AxisBindLockEnabledCheckBox != null)
            AxisBindLockEnabledCheckBox.Content = T("check.axisBindLockEnabled");
        if (AxisBindLockBindButton != null)
            AxisBindLockBindButton.Content = T("btn.axisBindLock");
        if (AxisBindLockClearButton != null)
            AxisBindLockClearButton.Content = T("btn.axisBindReset");
        if (AxisBindLockHintTextBlock != null)
            AxisBindLockHintTextBlock.Text = T("hint.axisBindLock");
        UpdateAxisBindLockStatusText();

        InputTab.Header = T("tab.input");
        FiltersTab.Header = T("tab.filters");
        MonitorTab.Header = T("tab.monitor");
        SettingsTab.Header = T("tab.settings");
        InputSourceHeaderTextBlock.Text = T("header.inputSource");
        CalibrationHeaderTextBlock.Text = T("header.calibration");
        DeviceLabelTextBlock.Text = T("label.device");
        AxisLabelTextBlock.Text = T("label.axis");
        PollingLabelTextBlock.Text = T("label.polling");
        OutputSinkLabelTextBlock.Text = T("label.output");
        ProfileLabelTextBlock.Text = T("label.profileName");
        CalibrationMinLabelTextBlock.Text = T("label.min");
        CalibrationCenterLabelTextBlock.Text = T("label.center");
        CalibrationMaxLabelTextBlock.Text = T("label.max");
        DeadzoneHeaderTextBlock.Text = T("header.deadzone");
        MedianHeaderTextBlock.Text = T("header.medianHampel");
        SpikeHeaderTextBlock.Text = T("header.spike");
        DeadzoneEnabledCheckBox.Content = T("check.enabled");
        DeadzoneDynamicCheckBox.Content = T("check.deadzoneDynamic");
        MedianEnabledCheckBox.Content = T("check.medianEnabled");
        HampelEnabledCheckBox.Content = T("check.hampelEnabled");
        SpikeGateEnabledCheckBox.Content = T("check.spikeEnabled");
        if (RadialSpikeZonesCheckBox != null)
            RadialSpikeZonesCheckBox.Content = T("check.radialSpike");
        if (CenterZoneLabelTextBlock != null)
            CenterZoneLabelTextBlock.Text = T("label.centerZone");
        if (CenterSpikeSensitivityLabelTextBlock != null)
            CenterSpikeSensitivityLabelTextBlock.Text = T("label.centerSpikeSensitivity");
        if (CenterSmoothingMultiplierLabelTextBlock != null)
            CenterSmoothingMultiplierLabelTextBlock.Text = T("label.centerSmoothingMultiplier");
        if (CenterSmoothingMultiplierHintTextBlock != null)
            CenterSmoothingMultiplierHintTextBlock.Text = T("hint.centerSmoothingMultiplier");
        if (ZImpulseGuardHeaderTextBlock != null)
            ZImpulseGuardHeaderTextBlock.Text = T("header.zImpulseGuard");
        if (ZImpulseGuardEnabledCheckBox != null)
            ZImpulseGuardEnabledCheckBox.Content = T("check.zImpulseGuardEnabled");
        if (ZImpulseCenterRadiusLabelTextBlock != null)
            ZImpulseCenterRadiusLabelTextBlock.Text = T("label.zImpulseCenterRadius");
        if (ZImpulseThresholdLabelTextBlock != null)
            ZImpulseThresholdLabelTextBlock.Text = T("label.zImpulseThreshold");
        if (ZImpulseConfirmLabelTextBlock != null)
            ZImpulseConfirmLabelTextBlock.Text = T("label.zImpulseConfirm");
        if (ZImpulseGuardHintTextBlock != null)
            ZImpulseGuardHintTextBlock.Text = T("hint.zImpulseGuard");
        if (AxisIntentHeaderTextBlock != null)
            AxisIntentHeaderTextBlock.Text = T("header.axisIntent");
        if (AxisIntentEnabledCheckBox != null)
            AxisIntentEnabledCheckBox.Content = T("check.axisIntentEnabled");
        if (AxisIntentDeflectionLabelTextBlock != null)
            AxisIntentDeflectionLabelTextBlock.Text = T("label.axisIntentDeflection");
        if (AxisIntentStrongLabelTextBlock != null)
            AxisIntentStrongLabelTextBlock.Text = T("label.axisIntentStrong");
        if (AxisIntentConfirmLabelTextBlock != null)
            AxisIntentConfirmLabelTextBlock.Text = T("label.axisIntentConfirm");
        if (AxisIntentHintTextBlock != null)
            AxisIntentHintTextBlock.Text = T("hint.axisIntent");
        if (CrossAxisShieldHeaderTextBlock != null)
            CrossAxisShieldHeaderTextBlock.Text = T("header.crossAxisShield");
        if (CrossAxisShieldEnabledCheckBox != null)
            CrossAxisShieldEnabledCheckBox.Content = T("check.crossAxisShieldEnabled");
        if (CrossAxisTargetLabelTextBlock != null)
            CrossAxisTargetLabelTextBlock.Text = T("label.crossAxisTarget");
        if (CrossAxisWatchLabelTextBlock != null)
            CrossAxisWatchLabelTextBlock.Text = T("label.crossAxisWatch");
        if (CrossAxisDeflectionLabelTextBlock != null)
            CrossAxisDeflectionLabelTextBlock.Text = T("label.crossAxisDeflection");
        if (CrossAxisDominanceCheckBox != null)
            CrossAxisDominanceCheckBox.Content = T("check.crossAxisDominance");
        if (CrossAxisDominanceLabelTextBlock != null)
            CrossAxisDominanceLabelTextBlock.Text = T("label.crossAxisDominance");
        if (CrossAxisRespectIntentCheckBox != null)
            CrossAxisRespectIntentCheckBox.Content = T("check.crossAxisRespectIntent");
        if (CrossAxisVelocityMinLabelTextBlock != null)
            CrossAxisVelocityMinLabelTextBlock.Text = T("label.crossAxisVelMin");
        if (CrossAxisVelocityMaxLabelTextBlock != null)
            CrossAxisVelocityMaxLabelTextBlock.Text = T("label.crossAxisVelMax");
        if (CrossAxisRateMulLabelTextBlock != null)
            CrossAxisRateMulLabelTextBlock.Text = T("label.crossAxisRateMul");
        if (CrossAxisEmaMulLabelTextBlock != null)
            CrossAxisEmaMulLabelTextBlock.Text = T("label.crossAxisEmaMul");
        if (CrossAxisHardLockCheckBox != null)
            CrossAxisHardLockCheckBox.Content = T("check.crossAxisHardLock");
        if (CrossAxisHardLockCenterCheckBox != null)
            CrossAxisHardLockCenterCheckBox.Content = T("check.crossAxisHardLockCenter");
        if (CrossAxisLeakLabelTextBlock != null)
            CrossAxisLeakLabelTextBlock.Text = T("label.crossAxisLeakMul");
        if (CrossAxisShieldHintTextBlock != null)
            CrossAxisShieldHintTextBlock.Text = T("hint.crossAxisShield");
        UpdateSpikeLabelMode();
        if (RadialSpikeHintTextBlock != null)
            RadialSpikeHintTextBlock.Text = T("hint.radialSpike");
        RateLimiterEnabledCheckBox.Content = T("check.rateEnabled");
        EmaEnabledCheckBox.Content = T("check.emaEnabled");
        DeadzoneRadiusLabelTextBlock.Text = T("label.deadzoneRadius");
        MedianWindowLabelTextBlock.Text = T("label.medianWindow");
        HampelWindowLabelTextBlock.Text = T("label.hampelWindow");
        HampelSigmaLabelTextBlock.Text = T("label.hampelSigma");
        SpikeDeltaLabelTextBlock.Text = T("label.spikeDelta");
        SpikeVelocityLabelTextBlock.Text = T("label.spikeVelocity");
        RateLimiterLabelTextBlock.Text = T("label.rateLimiter");
        EmaAlphaLabelTextBlock.Text = T("label.emaAlpha");
        SpikeHoldLabelTextBlock.Text = T("label.spikeHold");
        SpikeHintTextBlock.Text = T("hint.spikeHold");
        DeadzoneHintTextBlock.Text = T("hint.deadzone");
        MedianHampelHintTextBlock.Text = T("hint.medianHampel");
        RateLimiterHintTextBlock.Text = T("hint.rateLimiter");
        EmaHintTextBlock.Text = T("hint.ema");
        RawMetricLabelTextBlock.Text = T("metric.raw");
        FilteredMetricLabelTextBlock.Text = T("metric.filtered");
        DeltaMetricLabelTextBlock.Text = T("metric.delta");
        SpikeMetricLabelTextBlock.Text = T("metric.spikes");
        HampelMetricLabelTextBlock.Text = T("metric.hampel");
        SequenceMetricLabelTextBlock.Text = T("metric.sample");
        if (MonitorAxesHeaderTextBlock != null)
            MonitorAxesHeaderTextBlock.Text = T("monitor.axesTitle");
        UpdateChartLegend();
        SettingsHeaderTextBlock.Text = T("settings.header");
        EnableLoggingCheckBox.Content = T("settings.logging");
        if (ResetLogOnStartupCheckBox != null)
            ResetLogOnStartupCheckBox.Content = T("settings.resetLogOnStartup");
        if (ClearLogButton != null)
            ClearLogButton.Content = T("settings.clearLog");
        ShowHintsCheckBox.Content = T("settings.hints");
        AutoApplyInAppCheckBox.Content = T("settings.autoApply");
        StartWithWindowsCheckBox.Content = T("settings.startup");
        ResetAppliedButton.Content = T("settings.reset");
        DefaultSettingsButton.Content = T("settings.defaults");
        UpdateProfileTitle();

        SetStatus("status.language");
    }

    private async Task RefreshDevicesAsync()
    {
        try
        {
            var devices = await _inputProvider.GetDevicesAsync().ConfigureAwait(true);
            _devices.Clear();
            foreach (var item in devices)
                _devices.Add(item);

            if (_devices.Count > 0)
            {
                DeviceComboBox.SelectedIndex = 0;
                SetStatus("status.devices", _devices.Count);
            }
            else
            {
                _axes.Clear();
                SetStatus("status.noDevices");
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = ex.Message;
        }
    }

    private async Task RefreshAxesAsync()
    {
        if (DeviceComboBox.SelectedValue is not string deviceId || string.IsNullOrWhiteSpace(deviceId))
        {
            _axes.Clear();
            return;
        }

        var axes = await _inputProvider.GetAxesAsync(deviceId).ConfigureAwait(true);
        _axes.Clear();
        foreach (var axis in axes)
            _axes.Add(axis);

        var axisId = NormalizeAxisId(_profile.AxisName);
        var index = _axes.FindIndex(item => item.Id == axisId);
        AxisComboBox.SelectedIndex = index >= 0 ? index : (_axes.Count > 0 ? 0 : -1);
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _filterSaveTimer?.Stop();
            _filterSessionDirty = false;

            _preferences = await _preferencesStore.LoadAsync().ConfigureAwait(true);
            _diagnostics.Enabled = _preferences.LoggingEnabled;
            ApplyPreferencesToUi();
            if (_preferences.ResetLogOnStartup)
                _diagnostics.ResetLogFile("startup");
            _diagnostics.LogAppEvent(
                "app-start",
                $"version={AppVersion.Display}; data={AppDataPaths.BaseDirectory}; filters={AppDataPaths.FiltersFilePath}; log={_diagnostics.LogFilePath}");
            ApplyLanguageFromPreferences();
            SelectOutputSink(string.IsNullOrWhiteSpace(_preferences.OutputSink)
                ? (_vJoySink.IsAvailable ? "vJoy" : "Monitor")
                : _preferences.OutputSink);
            _vJoySink.Configure((uint)Math.Clamp(_preferences.VJoyDeviceId, 1, 16));
            VJoyOutputSink.TryReleaseStale((uint)Math.Clamp(_preferences.VJoyDeviceId, 1, 16));
            LoadProfilesListFromDisk();
            await RefreshDevicesAsync().ConfigureAwait(true);

            var sessionRestored = await TryRestoreFilterSessionAsync().ConfigureAwait(true);
            if (!sessionRestored &&
                _preferences.AutoApplyInApp &&
                HasCommittedAppliedProfile())
                await AutoApplyProfileAsync(startStream: false).ConfigureAwait(true);

            await TryRunBackgroundStreamWorkflowAsync().ConfigureAwait(true);
            LogCurrentSettings();
            _diagnostics.LogAppEvent("ui-ready", "main-window loaded");
            BindMonitorChartAxes();
            UpdateChartLegend();

            _filterSaveTimer?.Stop();
            _filterSessionDirty = false;
            _suppressFilterSessionSave = false;

            if (App.IsAgentMode)
            {
                ShowInTaskbar = false;
                Hide();
            }

            UpdateMonitorUiEnabled();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = ex.Message;
            _diagnostics.LogAppEvent("load-preferences-error", ex.Message);
        }
    }

    private void InputProviderOnReadingAvailable(object? sender, InputReadingEventArgs e)
    {
        try
        {
            InputProviderOnReadingAvailableCore(e);
        }
        catch (Exception ex)
        {
            _diagnostics.LogAppEvent("input-poll-error", ex.Message);
        }
    }

    private void InputProviderOnReadingAvailableCore(InputReadingEventArgs e)
    {
        var calibrated = _profile.Calibration.Normalize(e.RawValue);
        var shield = _pipeline.Settings.CrossAxisShield;
        var pollHz = Math.Max(20, _streamPollingHz);
        var pollDt = _lastPollTimestamp == default
            ? 1.0 / pollHz
            : (e.Timestamp - _lastPollTimestamp).TotalSeconds;
        _lastPollTimestamp = e.Timestamp;

        var targetAxis = ResolveShieldTargetAxisId(shield.TargetAxisId, e.AxisId);
        var otherAxesActive = EvaluateOtherAxesActive(e);
        var otherPeak = GetOtherAxesPeakDeflection(e);
        var targetIntentActive = _axisIntentTracker.Update(_pipeline.Settings.AxisIntent, calibrated, otherAxesActive);
        _pipeline.TargetAxisIntentActive = targetIntentActive && !otherAxesActive;
        var blockStrength = CrossAxisShieldPolicy.ComputeBlockStrength(
            shield,
            otherAxesActive,
            targetIntentActive,
            otherPeak,
            _otherAxisSustainedSamples,
            calibrated,
            e.AllAxes,
            targetAxis);
        if (shield.HardLockWhenActive &&
            otherAxesActive &&
            otherPeak >= Math.Clamp(shield.OtherAxisDeflectionThreshold, 0.01, 0.95) * 0.85)
            blockStrength = 1.0;

        var baseRateLimit = _pipeline.Settings.RateLimiter.MaxDeltaPerSecond;
        var baseEmaAlpha = _pipeline.Settings.Ema.Alpha;
        _pipeline.CrossAxisBlockStrength = blockStrength;
        if (blockStrength > 0.01)
        {
            var tighten = Math.Clamp(blockStrength, 0.0, 1.0);
            var rateMul = Lerp(shield.RateLimitMultiplierWhenActive, 0.06, tighten);
            _pipeline.Settings.RateLimiter.MaxDeltaPerSecond = baseRateLimit * rateMul;
            _pipeline.Settings.Ema.Alpha = Math.Clamp(
                baseEmaAlpha * Lerp(shield.EmaAlphaMultiplierWhenActive, 0.18, tighten),
                0.01,
                1.0);
        }

        var output = _pipeline.Process(calibrated) with { TargetAxisId = e.AxisId, AllAxes = e.AllAxes };
        var anchor = shield.HardLockForceCenter ? 0.0 : _crossAxisLockSmoother.Output;
        var smoothed = _crossAxisLockSmoother.Process(
            shield,
            blockStrength,
            output.FilteredValue,
            anchor,
            pollDt);
        smoothed = CrossAxisShieldPolicy.ApplyHardLockOutput(shield, blockStrength, smoothed, anchor);
        if (shield.HardLockWhenActive && blockStrength >= 0.20 && Math.Abs(smoothed) < 0.006)
            smoothed = anchor;

        var bindLock = _pipeline.Settings.AxisBindLock;
        var bindLockWasActive = _axisBindLockActive;
        _axisBindLockActive = AxisBindLockEvaluator.UpdateLockActive(
            bindLock,
            e.BindLockPressed,
            ref _axisBindLockToggleLatched,
            ref _axisBindLockPreviousPressed);
        if (bindLockWasActive != _axisBindLockActive && bindLock.Enabled)
            Dispatcher.BeginInvoke(UpdateAxisBindLockStatusText);
        smoothed = AxisBindLockEvaluator.ApplyLock(smoothed, _axisBindLockActive, bindLock.LockAnchor);
        output = output with { FilteredValue = smoothed };

        if (blockStrength > 0.01)
        {
            _pipeline.Settings.RateLimiter.MaxDeltaPerSecond = baseRateLimit;
            _pipeline.Settings.Ema.Alpha = baseEmaAlpha;
        }
        _pipeline.CrossAxisBlockStrength = 0.0;
        _latestRaw = output.RawValue;
        _latestFiltered = output.FilteredValue;
        _latestSequence = output.Sequence;
        if (e.AllAxes != null && e.AllAxes.Count > 0)
            _latestAllAxes = new Dictionary<string, double>(e.AllAxes, StringComparer.OrdinalIgnoreCase);

        if (_isRunning)
        {
        lock (_historySync)
        {
                _rawHistory.Add(output.RawValue);
                _filteredHistory.Add(output.FilteredValue);
                if (e.AllAxes is { Count: > 0 })
                    _monitorChartAxes.RecordSample(GetActiveChartAxisId(), e.AllAxes);
            }
        }

        if (_isRunning && _activeOutputSink != null)
            _activeOutputSink.Publish(output);

        var currentSpikeCount = _pipeline.Statistics.SpikeSuppressedCount;
        var currentHampelCount = _pipeline.Statistics.HampelOutlierCount;
        var (chartStream, chartOverlay) = GetMonitorChartLogAxes();
        _diagnostics.LogMovement(
            output.Sequence,
            output.RawValue,
            output.FilteredValue,
            blockStrength,
            otherAxesActive,
            targetIntentActive,
            otherPeak,
            e.AllAxes,
            currentSpikeCount - _lastLoggedSpikeCount,
            currentHampelCount - _lastLoggedHampelCount,
            chartStream,
            chartOverlay,
            _axisBindLockActive);
        _lastLoggedSpikeCount = currentSpikeCount;
        _lastLoggedHampelCount = currentHampelCount;

        if (_isRunning)
        {
            if (Math.Abs(output.RawValue) >= 0.995)
                _saturatedRawSamples++;
            else
                _saturatedRawSamples = 0;

            if (!_saturationWarned && _saturatedRawSamples > 180)
            {
                var side = output.RawValue < 0 ? "-1.0" : "1.0";
                SetStatus("status.saturatedInput", side);
                _diagnostics.LogAppEvent("saturated-input-warning", $"raw={output.RawValue:0.0000}; axis={e.AxisId}");
                _saturationWarned = true;
            }
        }
    }

    private bool EvaluateOtherAxesActive(InputReadingEventArgs e)
    {
        var shield = _pipeline.Settings.CrossAxisShield;
        if (!shield.Enabled)
            return false;

        var currentAxes = e.AllAxes;
        if (currentAxes == null || currentAxes.Count == 0)
            return false;

        var lastAxes = _lastAxesSnapshot;
        var lastTs = _lastAxesSnapshotTimestamp;
        _lastAxesSnapshot = new Dictionary<string, double>(currentAxes, StringComparer.OrdinalIgnoreCase);
        _lastAxesSnapshotTimestamp = e.Timestamp;

        var targetAxis = ResolveShieldTargetAxisId(shield.TargetAxisId, e.AxisId);
        var dt = lastAxes == null ? 0.0 : (e.Timestamp - lastTs).TotalSeconds;
        return CrossAxisShieldPolicy.EvaluateOtherAxesActive(
            shield,
            targetAxis,
            e.AxisId,
            currentAxes,
            lastAxes,
            dt,
            ref _crossAxisActivityLatched,
            ref _otherAxisSustainedSamples);
    }

    private double GetOtherAxesPeakDeflection(InputReadingEventArgs e)
    {
        var shield = _pipeline.Settings.CrossAxisShield;
        if (e.AllAxes == null || e.AllAxes.Count == 0)
            return 0.0;

        var targetAxis = ResolveShieldTargetAxisId(shield.TargetAxisId, e.AxisId);
        return CrossAxisShieldActivity.GetPeakDeflection(shield, targetAxis, e.AllAxes);
    }

    private static double Lerp(double from, double to, double t) => from + ((to - from) * Math.Clamp(t, 0.0, 1.0));

    private string ResolveShieldTargetAxisId(string configuredTargetAxisId, string fallbackAxisId)
    {
        if (configuredTargetAxisId.Equals("SELECTED", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(_streamAxisId) ? fallbackAxisId : _streamAxisId;

        return configuredTargetAxisId;
    }

    private void RefreshMonitor()
    {
        if (!IsMonitorUiActive())
            return;

        if (RawValueTextBlock == null || FilteredValueTextBlock == null || DeltaValueTextBlock == null ||
            SpikeCountTextBlock == null || HampelCountTextBlock == null || SequenceTextBlock == null)
            return;

        RawValueTextBlock.Text = _latestRaw.ToString("0.000");
        FilteredValueTextBlock.Text = _latestFiltered.ToString("0.000");
        DeltaValueTextBlock.Text = (_latestFiltered - _latestRaw).ToString("0.000");
        SpikeCountTextBlock.Text = _pipeline.Statistics.SpikeSuppressedCount.ToString();
        HampelCountTextBlock.Text = _pipeline.Statistics.HampelOutlierCount.ToString();
        SequenceTextBlock.Text = _latestSequence.ToString();

        var (chartStream, chartOverlay) = GetMonitorChartLogAxes();
        _diagnostics.LogMonitorSnapshot(
            _latestRaw,
            _latestFiltered,
            _pipeline.Statistics.SpikeSuppressedCount,
            _pipeline.Statistics.HampelOutlierCount,
            _latestSequence,
            chartStream,
            chartOverlay,
            _latestAllAxes);

        RenderChart();
    }

    private void BindMonitorChartAxes()
    {
        if (_monitorChartAxes.IsBound)
            return;

        var pairs = new (string Id, Polyline? Line)[]
        {
            ("X", AxisXPolyline),
            ("Y", AxisYPolyline),
            ("Z", AxisZPolyline),
            ("RX", AxisRxPolyline),
            ("RY", AxisRyPolyline),
            ("RZ", AxisRzPolyline),
            ("SL0", AxisSl0Polyline),
            ("SL1", AxisSl1Polyline),
        };

        foreach (var (id, line) in pairs)
        {
            if (line == null)
            {
                _diagnostics.LogAppEvent("chart-bind-missing", $"polyline {id} is null");
                return;
            }

            _monitorChartAxes.BindPolyline(id, line);
        }

        _diagnostics.LogAppEvent("chart-bind-ok", $"axis polylines bound; xCount={_monitorChartAxes.GetHistoryCount("X")}");
    }

    private string GetActiveChartAxisId()
    {
        if (!string.IsNullOrWhiteSpace(_streamAxisId))
            return _streamAxisId;

        if (AxisComboBox?.SelectedValue is string axisId && !string.IsNullOrWhiteSpace(axisId))
            return axisId;

        return "RZ";
    }

    private (string StreamAxisId, IReadOnlyList<string> OverlayAxisIds) GetMonitorChartLogAxes()
    {
        var stream = GetActiveChartAxisId().ToUpperInvariant();
        return (stream, _monitorChartAxes.GetVisibleOverlayAxisIds(stream));
    }

    private void LogMonitorChartAxesSelection(string reason)
    {
        var (stream, overlay) = GetMonitorChartLogAxes();
        _diagnostics.LogAppEvent(
            "chart-axes",
            $"{reason}; chartStream={stream}; chartOverlay={MonitorChartAxes.FormatAxisIdList(overlay)}");
    }

    private void RenderChart()
    {
        if (ChartCanvas == null || RawPolyline == null || FilteredPolyline == null)
            return;

        BindMonitorChartAxes();

        var width = ChartCanvas.ActualWidth;
        var height = ChartCanvas.ActualHeight;
        if (width <= 10)
            width = ChartCanvas.Width;
        if (height <= 10)
            height = ChartCanvas.Height;
        if (width <= 10 || height <= 10)
            return;

        int rawCount;
        int filteredCount;
        lock (_historySync)
        {
            rawCount = _rawHistory.CopyOrdered(_rawChartScratch);
            filteredCount = _filteredHistory.CopyOrdered(_filteredChartScratch);
            RawPolyline.Points = BuildPoints(_rawChartScratch, rawCount, width, height);
            FilteredPolyline.Points = BuildPoints(_filteredChartScratch, filteredCount, width, height);
            _monitorChartAxes.Render(width, height, GetActiveChartAxisId());
        }
    }

    private void MonitorTab_OnLoaded(object sender, RoutedEventArgs e)
    {
        BindMonitorChartAxes();
        EnsureMonitorAxisToggles();
        SyncMonitorAxisToggles();
        UpdateChartLegend();
        LogMonitorChartAxesSelection("monitor-tab");
        RenderChart();
    }

    private void EnsureMonitorAxisToggles()
    {
        if (_monitorAxisTogglesBuilt || MonitorAxisTogglesPanel == null)
            return;

        foreach (var axisId in MonitorChartAxes.AxisOrder)
        {
            var brush = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(MonitorChartAxes.GetAxisColor(axisId))!);
            var check = new CheckBox
            {
                Tag = axisId,
                Content = axisId,
                Foreground = brush,
                IsChecked = true,
                Margin = new Thickness(0, 0, 14, 4),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            check.Checked += MonitorAxisToggle_Changed;
            check.Unchecked += MonitorAxisToggle_Changed;
            _monitorAxisToggles[axisId] = check;
            MonitorAxisTogglesPanel.Children.Add(check);
        }

        _monitorAxisTogglesBuilt = true;
    }

    private void SyncMonitorAxisToggles()
    {
        EnsureMonitorAxisToggles();
        if (!_monitorAxisTogglesBuilt)
            return;

        var selected = GetActiveChartAxisId().ToUpperInvariant();
        _suppressMonitorAxisToggle = true;
        try
        {
            foreach (var axisId in MonitorChartAxes.AxisOrder)
            {
                if (!_monitorAxisToggles.TryGetValue(axisId, out var check))
                    continue;

                var isSelected = axisId.Equals(selected, StringComparison.OrdinalIgnoreCase);
                check.IsEnabled = !isSelected;
                check.ToolTip = isSelected ? T("monitor.axisOnOutput") : null;
                check.IsChecked = isSelected || _monitorChartAxes.IsAxisEnabled(axisId);
            }
        }
        finally
        {
            _suppressMonitorAxisToggle = false;
        }
    }

    private string GetDeviceDisplayName(string deviceId)
    {
        var device = _devices.FirstOrDefault(d => d.Id.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
        return device?.DisplayName ?? deviceId;
    }

    private void UpdateAxisBindLockStatusText()
    {
        if (AxisBindLockStatusTextBlock == null)
            return;

        var bind = _pipeline.Settings.AxisBindLock;
        if (!AxisBindLockEvaluator.HasBindAssignment(bind))
        {
            AxisBindLockStatusTextBlock.Text = T("label.axisBindNotSet");
            return;
        }

        var text = FormatAxisBindLockStatus(bind);
        if (bind.Enabled && _isRunning)
            text += _axisBindLockActive ? T("label.axisBindLocked") : T("label.axisBindUnlocked");

        AxisBindLockStatusTextBlock.Text = text;
    }

    private string FormatAxisBindLockStatus(AxisBindLockSettings bind)
    {
        if (string.Equals(bind.BindDeviceKind, BindInputDeviceKinds.Keyboard, StringComparison.OrdinalIgnoreCase))
            return string.Format(T("label.axisBindKeyboard"), BindInputFormatting.GetKeyName(bind.KeyCode));

        if (string.Equals(bind.BindDeviceKind, BindInputDeviceKinds.Mouse, StringComparison.OrdinalIgnoreCase))
            return string.Format(T("label.axisBindMouse"), BindInputFormatting.GetMouseButtonName(bind.ButtonIndex));

        var deviceName = GetDeviceDisplayName(bind.BindDeviceId);
        return string.Format(T("label.axisBindJoystick"), deviceName, bind.ButtonIndex);
    }

    private BindLockPollConfig? CreateBindLockPollConfig()
    {
        var bind = _pipeline.Settings.AxisBindLock;
        return BindInputFormatting.CreatePollConfig(
            bind.Enabled,
            bind.BindDeviceKind,
            bind.BindDeviceId,
            bind.ButtonIndex,
            bind.KeyCode);
    }

    private async void AxisBindLockBindButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (AxisBindLockBindButton == null)
            return;

        AxisBindLockBindButton.IsEnabled = false;
        SetStatus("status.bindAxisWaiting");
        try
        {
            await RefreshDevicesAsync().ConfigureAwait(true);
            var hz = Math.Max(20, (int)PollingSlider.Value);
            var capture = await _inputProvider.CaptureNextInputBindAsync(5000, hz).ConfigureAwait(true);
            if (capture == null)
            {
                SetStatus("status.bindAxisFail");
                return;
            }

            _pipeline.Settings.AxisBindLock.BindDeviceKind = capture.DeviceKind;
            _pipeline.Settings.AxisBindLock.BindDeviceId = capture.DeviceId;
            _pipeline.Settings.AxisBindLock.ButtonIndex = capture.ButtonIndex;
            _pipeline.Settings.AxisBindLock.KeyCode = capture.KeyCode;
            _pipeline.Settings.AxisBindLock.ToggleMode = true;
            _pipeline.Settings.AxisBindLock.Enabled = true;
            if (AxisBindLockEnabledCheckBox != null)
                AxisBindLockEnabledCheckBox.IsChecked = true;
            _axisBindLockToggleLatched = false;
            _axisBindLockPreviousPressed = false;
            _axisBindLockActive = false;
            OnUserFilterChanged();
            RefreshBindLockPollingIfRunning();
            UpdateAxisBindLockStatusText();
            ScheduleFilterSessionSave();

            SetBindCaptureStatus(capture);
            _diagnostics.LogAppEvent(
                "axis-bind-lock",
                $"kind={capture.DeviceKind}; device={capture.DeviceId}; button={capture.ButtonIndex}; key={capture.KeyCode}");
        }
        catch (Exception ex)
        {
            SetStatus("status.bindAxisFail");
            _diagnostics.LogAppEvent("axis-bind-lock-error", ex.Message);
        }
        finally
        {
            AxisBindLockBindButton.IsEnabled = true;
        }
    }

    private void SetBindCaptureStatus(InputBindCapture capture)
    {
        if (string.Equals(capture.DeviceKind, BindInputDeviceKinds.Keyboard, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("status.bindAxisDoneKeyboard", BindInputFormatting.GetKeyName(capture.KeyCode));
            return;
        }

        if (string.Equals(capture.DeviceKind, BindInputDeviceKinds.Mouse, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("status.bindAxisDoneMouse", BindInputFormatting.GetMouseButtonName(capture.ButtonIndex));
            return;
        }

        SetStatus("status.bindAxisDoneJoystick", GetDeviceDisplayName(capture.DeviceId), capture.ButtonIndex);
    }

    private void AxisBindLockClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        _pipeline.Settings.AxisBindLock.BindDeviceKind = string.Empty;
        _pipeline.Settings.AxisBindLock.BindDeviceId = string.Empty;
        _pipeline.Settings.AxisBindLock.ButtonIndex = -1;
        _pipeline.Settings.AxisBindLock.KeyCode = -1;
        _axisBindLockToggleLatched = false;
        _axisBindLockPreviousPressed = false;
        _axisBindLockActive = false;
        OnUserFilterChanged();
        RefreshBindLockPollingIfRunning();
        UpdateAxisBindLockStatusText();
        ScheduleFilterSessionSave();
        _diagnostics.LogAppEvent("axis-bind-lock", "button=cleared");
    }

    private void RefreshBindLockPollingIfRunning()
    {
        if (!_isRunning)
            return;

        if (DeviceComboBox.SelectedValue is not string deviceId || string.IsNullOrWhiteSpace(deviceId))
            return;

        if (AxisComboBox.SelectedValue is not string axisId || string.IsNullOrWhiteSpace(axisId))
            return;

        try
        {
            _inputProvider.Start(deviceId, axisId, _streamPollingHz, CreateBindLockPollConfig());
            _diagnostics.LogAppEvent("axis-bind-lock", "poll-config=refreshed");
        }
        catch (Exception ex)
        {
            _diagnostics.LogAppEvent("axis-bind-lock-error", $"poll-refresh: {ex.Message}");
        }
    }

    private void MonitorAxisToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressMonitorAxisToggle || sender is not CheckBox check || check.Tag is not string axisId)
            return;

        _monitorChartAxes.SetAxisEnabled(axisId, check.IsChecked == true);
        LogMonitorChartAxesSelection($"toggle {axisId}={(check.IsChecked == true ? 1 : 0)}");
        UpdateChartLegend();
        RenderChart();
        ScheduleFilterSessionSave();
    }

    private void UpdateChartLegend()
    {
        if (ChartLegendPanel == null)
            return;

        ChartLegendPanel.Children.Clear();

        var streamAxis = GetActiveChartAxisId().ToUpperInvariant();
        AddChartLegendLine($"{T("metric.raw")} ({streamAxis})", "#EF4444", fontWeight: FontWeights.SemiBold);
        AddChartLegendLine($"{T("metric.filtered")} ({streamAxis})", "#22C55E", fontWeight: FontWeights.Bold);

        foreach (var axisId in MonitorChartAxes.AxisOrder)
        {
            if (axisId.Equals(streamAxis, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!_monitorChartAxes.IsAxisEnabled(axisId))
                continue;

            AddChartLegendLine(
                $"{axisId} — {T("monitor.legendDeviceRaw")}",
                MonitorChartAxes.GetAxisColor(axisId));
        }

        SyncMonitorAxisToggles();
    }

    private void AddChartLegendLine(string text, string colorHex, FontWeight? fontWeight = null)
    {
        ChartLegendPanel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)!),
            FontSize = 11,
            FontWeight = fontWeight ?? FontWeights.Normal,
            Margin = new Thickness(0, 0, 0, 3),
        });
    }

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

    private void MainTabControl_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MonitorTab.IsSelected)
        {
            BindMonitorChartAxes();
            EnsureMonitorAxisToggles();
            UpdateChartLegend();
            RenderChart();
        }

        UpdateMonitorUiEnabled();
    }

    private bool IsMonitorUiActive()
    {
        if (App.IsAgentMode)
            return false;
        if (!IsVisible)
            return false;

        return MonitorTab.IsSelected;
    }

    private void UpdateMonitorUiEnabled()
    {
        _monitorUiEnabled = IsMonitorUiActive();
        if (_monitorUiEnabled)
        {
            RefreshMonitor();
            Dispatcher.BeginInvoke(RenderChart, DispatcherPriority.Loaded);
        }
    }

    private void UpdateSpikeLabelMode()
    {
        if (SpikeDeltaLabelTextBlock == null || SpikeVelocityLabelTextBlock == null)
            return;

        var radial = RadialSpikeZonesCheckBox?.IsChecked == true;
        SpikeDeltaLabelTextBlock.Text = T(radial ? "label.outerSpikeDelta" : "label.spikeDelta");
        SpikeVelocityLabelTextBlock.Text = T(radial ? "label.outerSpikeVelocity" : "label.spikeVelocity");
    }

    private void RadialSpikeZonesCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        UpdateSpikeLabelMode();
        OnUserFilterChanged();
    }

    private static bool IsZLikeAxis(string? axisId)
    {
        if (string.IsNullOrWhiteSpace(axisId))
            return false;
        return axisId.Equals("Z", StringComparison.OrdinalIgnoreCase) ||
               axisId.Equals("RZ", StringComparison.OrdinalIgnoreCase);
    }

    private void AxisComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncFilterPipelineFromUi();
        UpdateChartLegend();
        if (_isRunning)
            LogMonitorChartAxesSelection("axis-combo");
        ScheduleFilterSessionSave();
    }

    private string[] BuildCrossAxisWatchedList()
    {
        var axes = new List<string>(6);
        if (CrossWatchXCheckBox?.IsChecked == true) axes.Add("X");
        if (CrossWatchYCheckBox?.IsChecked == true) axes.Add("Y");
        if (CrossWatchZCheckBox?.IsChecked == true) axes.Add("Z");
        if (CrossWatchRXCheckBox?.IsChecked == true) axes.Add("RX");
        if (CrossWatchRYCheckBox?.IsChecked == true) axes.Add("RY");
        if (CrossWatchRZCheckBox?.IsChecked == true) axes.Add("RZ");
        return axes.ToArray();
    }

    private void SyncFilterPipelineFromUi()
    {
        if (!IsFilterControlsReady())
            return;

        _pipeline.Settings.Deadzone.Enabled = DeadzoneEnabledCheckBox.IsChecked == true;
        _pipeline.Settings.Deadzone.Dynamic = DeadzoneDynamicCheckBox.IsChecked == true;
        _pipeline.Settings.Deadzone.Radius = DeadzoneRadiusSlider.Value;

        _pipeline.Settings.Median.Enabled = MedianEnabledCheckBox.IsChecked == true;
        _pipeline.Settings.Median.WindowSize = (int)MedianWindowSlider.Value;

        _pipeline.Settings.Hampel.Enabled = HampelEnabledCheckBox.IsChecked == true;
        _pipeline.Settings.Hampel.WindowSize = (int)HampelWindowSlider.Value;
        _pipeline.Settings.Hampel.SigmaMultiplier = HampelSigmaSlider.Value;

        _pipeline.Settings.SpikeGate.Enabled = SpikeGateEnabledCheckBox.IsChecked == true;
        _pipeline.Settings.SpikeGate.RadialZonesEnabled = RadialSpikeZonesCheckBox?.IsChecked == true;
        _pipeline.Settings.SpikeGate.CenterZoneEnd = CenterZoneSlider?.Value ?? 0.28;
        var centerDelta = CenterSpikeSensitivitySlider?.Value ?? 0.05;
        _pipeline.Settings.SpikeGate.CenterDeltaThreshold = centerDelta;
        _pipeline.Settings.SpikeGate.CenterVelocityThresholdPerSecond = centerDelta * 36.0;
        _pipeline.Settings.SpikeGate.CenterSmoothingMultiplier = CenterSmoothingMultiplierSlider?.Value ?? 1.0;
        var selectedAxisId = AxisComboBox.SelectedValue?.ToString();
        _pipeline.Settings.ZImpulseGuard.Enabled = (ZImpulseGuardEnabledCheckBox?.IsChecked == true) && IsZLikeAxis(selectedAxisId);
        _pipeline.Settings.ZImpulseGuard.CenterRadius = ZImpulseCenterRadiusSlider?.Value ?? 0.35;
        _pipeline.Settings.ZImpulseGuard.IntentThreshold = ZImpulseThresholdSlider?.Value ?? 0.06;
        _pipeline.Settings.ZImpulseGuard.ConfirmSamples = (int)(ZImpulseConfirmSlider?.Value ?? 2.0);
        _pipeline.Settings.AxisIntent.Enabled = AxisIntentEnabledCheckBox?.IsChecked == true;
        _pipeline.Settings.AxisIntent.DeflectionThreshold = AxisIntentDeflectionSlider?.Value ?? 0.07;
        _pipeline.Settings.AxisIntent.StrongDeflectionThreshold = AxisIntentStrongSlider?.Value ?? 0.20;
        _pipeline.Settings.AxisIntent.ConfirmSamples = (int)(AxisIntentConfirmSlider?.Value ?? 2.0);
        _pipeline.Settings.CrossAxisShield.Enabled = CrossAxisShieldEnabledCheckBox?.IsChecked == true;
        _pipeline.Settings.CrossAxisShield.TargetAxisId = (CrossAxisTargetComboBox?.SelectedValue?.ToString() ?? "SELECTED").ToUpperInvariant();
        _pipeline.Settings.CrossAxisShield.WatchedAxes = BuildCrossAxisWatchedList();
        _pipeline.Settings.CrossAxisShield.OtherAxisDeflectionThreshold = CrossAxisDeflectionSlider?.Value ?? 0.08;
        _pipeline.Settings.CrossAxisShield.RequireOtherAxisDominance = CrossAxisDominanceCheckBox?.IsChecked == true;
        _pipeline.Settings.CrossAxisShield.OtherAxisDominanceRatio = CrossAxisDominanceSlider?.Value ?? 1.15;
        _pipeline.Settings.CrossAxisShield.RespectTargetIntent = CrossAxisRespectIntentCheckBox?.IsChecked == true;
        var minVel = CrossAxisVelocityMinSlider?.Value ?? 0.25;
        var maxVel = CrossAxisVelocityMaxSlider?.Value ?? 30.0;
        if (maxVel < minVel)
            maxVel = minVel;
        _pipeline.Settings.CrossAxisShield.MinOtherAxisVelocityPerSecond = minVel;
        _pipeline.Settings.CrossAxisShield.MaxOtherAxisVelocityPerSecond = maxVel;
        _pipeline.Settings.CrossAxisShield.RateLimitMultiplierWhenActive = CrossAxisRateMulSlider?.Value ?? 0.55;
        _pipeline.Settings.CrossAxisShield.EmaAlphaMultiplierWhenActive = CrossAxisEmaMulSlider?.Value ?? 0.60;
        _pipeline.Settings.CrossAxisShield.HardLockWhenActive = CrossAxisHardLockCheckBox?.IsChecked == true;
        _pipeline.Settings.CrossAxisShield.HardLockForceCenter = CrossAxisHardLockCenterCheckBox?.IsChecked == true;
        _pipeline.Settings.CrossAxisShield.LockLeakMultiplier = CrossAxisLeakSlider?.Value ?? 0.08;

        _pipeline.Settings.AxisBindLock.Enabled = AxisBindLockEnabledCheckBox?.IsChecked == true;
        _pipeline.Settings.AxisBindLock.ToggleMode = true;

        if (_pipeline.Settings.SpikeGate.RadialZonesEnabled)
        {
            _pipeline.Settings.SpikeGate.OuterDeltaThreshold = SpikeDeltaSlider.Value;
            _pipeline.Settings.SpikeGate.OuterVelocityThresholdPerSecond = SpikeVelocitySlider.Value;
            _pipeline.Settings.SpikeGate.OuterMaxConsecutiveSuppressions = (int)SpikeHoldSlider.Value;
        }
        else
        {
        _pipeline.Settings.SpikeGate.DeltaThreshold = SpikeDeltaSlider.Value;
        _pipeline.Settings.SpikeGate.VelocityThresholdPerSecond = SpikeVelocitySlider.Value;
        _pipeline.Settings.SpikeGate.MaxConsecutiveSuppressions = (int)SpikeHoldSlider.Value;
        }

        _pipeline.Settings.RateLimiter.Enabled = RateLimiterEnabledCheckBox.IsChecked == true;
        _pipeline.Settings.RateLimiter.MaxDeltaPerSecond = RateLimiterSlider.Value;

        _pipeline.Settings.Ema.Enabled = EmaEnabledCheckBox.IsChecked == true;
        _pipeline.Settings.Ema.Alpha = EmaAlphaSlider.Value;
    }

    private void OnUserFilterChanged()
    {
        if (_suppressFilterSessionSave)
        {
            if (IsFilterControlsReady())
                SyncFilterPipelineFromUi();
            return;
        }

        _filterSessionDirty = true;
        SyncFilterPipelineFromUi();
        ScheduleFilterSessionSave();
    }

    private void SyncFilterUiText()
    {
        if (!IsFilterControlsReady())
            return;

        DeadzoneRadiusTextBlock.Text = DeadzoneRadiusSlider.Value.ToString("0.000");
        MedianWindowTextBlock.Text = ((int)MedianWindowSlider.Value).ToString();
        HampelWindowTextBlock.Text = ((int)HampelWindowSlider.Value).ToString();
        HampelSigmaTextBlock.Text = HampelSigmaSlider.Value.ToString("0.00");
        SpikeDeltaTextBlock.Text = SpikeDeltaSlider.Value.ToString("0.000");
        SpikeVelocityTextBlock.Text = SpikeVelocitySlider.Value.ToString("0.00");
        SpikeHoldTextBlock.Text = ((int)SpikeHoldSlider.Value).ToString();
        if (CenterZoneTextBlock != null)
            CenterZoneTextBlock.Text = CenterZoneSlider?.Value.ToString("0.00") ?? "0.28";
        if (CenterSpikeSensitivityTextBlock != null)
            CenterSpikeSensitivityTextBlock.Text = CenterSpikeSensitivitySlider?.Value.ToString("0.000") ?? "0.050";
        if (CenterSmoothingMultiplierTextBlock != null)
            CenterSmoothingMultiplierTextBlock.Text = (CenterSmoothingMultiplierSlider?.Value ?? 1.0).ToString("0.0");
        if (ZImpulseCenterRadiusTextBlock != null)
            ZImpulseCenterRadiusTextBlock.Text = (ZImpulseCenterRadiusSlider?.Value ?? 0.35).ToString("0.00");
        if (ZImpulseThresholdTextBlock != null)
            ZImpulseThresholdTextBlock.Text = (ZImpulseThresholdSlider?.Value ?? 0.06).ToString("0.000");
        if (ZImpulseConfirmTextBlock != null)
            ZImpulseConfirmTextBlock.Text = ((int)(ZImpulseConfirmSlider?.Value ?? 2.0)).ToString();
        if (AxisIntentDeflectionTextBlock != null)
            AxisIntentDeflectionTextBlock.Text = (AxisIntentDeflectionSlider?.Value ?? 0.07).ToString("0.00");
        if (AxisIntentStrongTextBlock != null)
            AxisIntentStrongTextBlock.Text = (AxisIntentStrongSlider?.Value ?? 0.20).ToString("0.00");
        if (AxisIntentConfirmTextBlock != null)
            AxisIntentConfirmTextBlock.Text = ((int)(AxisIntentConfirmSlider?.Value ?? 2.0)).ToString();
        if (CrossAxisDeflectionTextBlock != null)
            CrossAxisDeflectionTextBlock.Text = (CrossAxisDeflectionSlider?.Value ?? 0.08).ToString("0.00");
        if (CrossAxisDominanceTextBlock != null)
            CrossAxisDominanceTextBlock.Text = (CrossAxisDominanceSlider?.Value ?? 1.15).ToString("0.00");
        if (CrossAxisVelocityMinTextBlock != null)
            CrossAxisVelocityMinTextBlock.Text = (CrossAxisVelocityMinSlider?.Value ?? 0.25).ToString("0.00");
        if (CrossAxisVelocityMaxTextBlock != null)
            CrossAxisVelocityMaxTextBlock.Text = (CrossAxisVelocityMaxSlider?.Value ?? 30.0).ToString("0.00");
        if (CrossAxisRateMulTextBlock != null)
            CrossAxisRateMulTextBlock.Text = (CrossAxisRateMulSlider?.Value ?? 0.55).ToString("0.00");
        if (CrossAxisEmaMulTextBlock != null)
            CrossAxisEmaMulTextBlock.Text = (CrossAxisEmaMulSlider?.Value ?? 0.60).ToString("0.00");
        if (CrossAxisLeakTextBlock != null)
            CrossAxisLeakTextBlock.Text = (CrossAxisLeakSlider?.Value ?? 0.08).ToString("0.00");
        RateLimiterTextBlock.Text = RateLimiterSlider.Value.ToString("0.00");
        EmaAlphaTextBlock.Text = EmaAlphaSlider.Value.ToString("0.000");

        CalibrationMinTextBlock.Text = CalibrationMinSlider.Value.ToString("0.00");
        CalibrationCenterTextBlock.Text = CalibrationCenterSlider.Value.ToString("0.00");
        CalibrationMaxTextBlock.Text = CalibrationMaxSlider.Value.ToString("0.00");
    }

    private void LoadProfileIntoUi(TunerProfile profile)
    {
        ProfileNameTextBox.Text = profile.Name;
        PollingSlider.Value = profile.PollingHz;
        PollingValueTextBlock.Text = profile.PollingHz.ToString();
        SelectOutputSink(string.IsNullOrWhiteSpace(profile.OutputSink) ? "vJoy" : profile.OutputSink);
        _preferences.VJoyDeviceId = Math.Clamp(profile.VJoyDeviceId, 1, 16);
        _vJoySink.Configure((uint)_preferences.VJoyDeviceId);

        CalibrationMinSlider.Value = profile.Calibration.Min;
        CalibrationCenterSlider.Value = profile.Calibration.Center;
        CalibrationMaxSlider.Value = profile.Calibration.Max;

        _suppressFilterSessionSave = true;
        try
        {
            ApplyFilterSettings(FilterSettingsNormalizer.Ensure(profile.Filters));
        }
        finally
        {
            _suppressFilterSessionSave = false;
            _filterSessionDirty = false;
        }
    }

    private void ApplyFilterSettings(FilterSettings filters)
    {
        filters = FilterSettingsNormalizer.Ensure(filters);
        var snapshot = FilterSettingsSnapshot.Clone(filters);
        _pipeline.LoadSettings(snapshot);
        ApplyFiltersToUi(snapshot);
        UpdateAxisBindLockStatusText();
        _filterSessionDirty = false;
    }

    private FilterSettings CaptureFilterSettingsFromUi()
    {
        SyncFilterPipelineFromUi();
        return FilterSettingsSnapshot.Clone(_pipeline.Settings);
    }

    private FilterSessionState CaptureSessionFromUi()
    {
        SyncFilterPipelineFromUi();
        var selectedDevice = DeviceComboBox.SelectedItem as InputDeviceInfo;
        var selectedAxis = AxisComboBox.SelectedItem as InputAxisInfo;

        return new FilterSessionState
        {
            SchemaVersion = FilterSessionState.CurrentSchemaVersion,
            ProfileName = GetCurrentProfileName(),
            UserModified = true,
            Filters = FilterSettingsSnapshot.Clone(_pipeline.Settings),
            DeviceId = selectedDevice?.Id ?? string.Empty,
            DeviceName = selectedDevice?.DisplayName ?? string.Empty,
            AxisId = selectedAxis?.Id ?? NormalizeAxisId(_profile.AxisName),
            PollingHz = Math.Max(20, (int)PollingSlider.Value),
            OutputSink = GetSelectedOutputSinkKey(),
            VJoyDeviceId = Math.Clamp(_preferences.VJoyDeviceId, 1, 16),
            Calibration = new CalibrationSettings
            {
                Min = CalibrationMinSlider.Value,
                Center = CalibrationCenterSlider.Value,
                Max = CalibrationMaxSlider.Value
            },
            MonitorChartEnabledAxes = _monitorChartAxes.GetEnabledAxisIdsSnapshot(),
            Ui = CaptureUiPreferencesFromUi(),
        };
    }

    private SessionUiPreferences CaptureUiPreferencesFromUi()
    {
        SyncPreferencesFromUi();
        return new SessionUiPreferences
        {
            LoggingEnabled = _preferences.LoggingEnabled,
            ResetLogOnStartup = _preferences.ResetLogOnStartup,
            ShowFilterHints = _preferences.ShowFilterHints,
            AutoApplyInApp = _preferences.AutoApplyInApp,
            StartWithWindows = _preferences.StartWithWindows,
            UiLanguage = _preferences.UiLanguage,
        };
    }

    private void SyncPreferencesFromUi()
    {
        if (EnableLoggingCheckBox != null)
            _preferences.LoggingEnabled = EnableLoggingCheckBox.IsChecked == true;
        if (ResetLogOnStartupCheckBox != null)
            _preferences.ResetLogOnStartup = ResetLogOnStartupCheckBox.IsChecked == true;
        if (ShowHintsCheckBox != null)
            _preferences.ShowFilterHints = ShowHintsCheckBox.IsChecked == true;
        if (AutoApplyInAppCheckBox != null)
            _preferences.AutoApplyInApp = AutoApplyInAppCheckBox.IsChecked == true;
        if (StartWithWindowsCheckBox != null)
            _preferences.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        if (LanguageComboBox?.SelectedItem is ComboBoxItem langItem)
            _preferences.UiLanguage = (langItem.Content?.ToString() ?? "RU").ToUpperInvariant();
    }

    private async Task ApplySessionUiPreferencesAsync(SessionUiPreferences? ui)
    {
        if (ui == null)
            return;

        _preferences.LoggingEnabled = ui.LoggingEnabled;
        _preferences.ResetLogOnStartup = ui.ResetLogOnStartup;
        _preferences.ShowFilterHints = ui.ShowFilterHints;
        _preferences.AutoApplyInApp = ui.AutoApplyInApp;
        _preferences.StartWithWindows = ui.StartWithWindows;
        _preferences.UiLanguage = string.IsNullOrWhiteSpace(ui.UiLanguage) ? "RU" : ui.UiLanguage.ToUpperInvariant();
        _diagnostics.Enabled = _preferences.LoggingEnabled;

        ApplyPreferencesToUi();
        ApplyLanguageFromPreferences();
        await _preferencesStore.SaveAsync(_preferences).ConfigureAwait(true);
    }

    private async Task ApplySessionToUiAsync(FilterSessionState session)
    {
        if (!string.IsNullOrWhiteSpace(session.ProfileName))
            ProfileNameTextBox.Text = session.ProfileName;

        ApplyFilterSettings(session.Filters);

        PollingSlider.Value = Math.Clamp(session.PollingHz, 20, 500);
        PollingValueTextBlock.Text = ((int)PollingSlider.Value).ToString();

        SelectOutputSink(string.IsNullOrWhiteSpace(session.OutputSink) ? "vJoy" : session.OutputSink);
        _preferences.VJoyDeviceId = Math.Clamp(session.VJoyDeviceId, 1, 16);
        _vJoySink.Configure((uint)_preferences.VJoyDeviceId);

        CalibrationMinSlider.Value = session.Calibration.Min;
        CalibrationCenterSlider.Value = session.Calibration.Center;
        CalibrationMaxSlider.Value = session.Calibration.Max;

        _profile.Calibration = session.Calibration;

        if (!string.IsNullOrWhiteSpace(session.DeviceId))
        {
            await RefreshDevicesAsync().ConfigureAwait(true);
            DeviceComboBox.SelectedValue = session.DeviceId;
            await RefreshAxesAsync().ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(session.AxisId))
                AxisComboBox.SelectedValue = NormalizeAxisId(session.AxisId);
        }

        _monitorChartAxes.ApplyEnabledAxisSnapshot(session.MonitorChartEnabledAxes);
        EnsureMonitorAxisToggles();
        SyncMonitorAxisToggles();

        await ApplySessionUiPreferencesAsync(session.Ui).ConfigureAwait(true);

        UpdateProfileTitle();
        UpdateChartLegend();
        SyncFilterUiText();
    }

    private void ApplyFiltersToUi(FilterSettings filters)
    {
        DeadzoneEnabledCheckBox.IsChecked = filters.Deadzone.Enabled;
        DeadzoneDynamicCheckBox.IsChecked = filters.Deadzone.Dynamic;
        DeadzoneRadiusSlider.Value = filters.Deadzone.Radius;

        MedianEnabledCheckBox.IsChecked = filters.Median.Enabled;
        MedianWindowSlider.Value = filters.Median.WindowSize;

        HampelEnabledCheckBox.IsChecked = filters.Hampel.Enabled;
        HampelWindowSlider.Value = filters.Hampel.WindowSize;
        HampelSigmaSlider.Value = filters.Hampel.SigmaMultiplier;

        SpikeGateEnabledCheckBox.IsChecked = filters.SpikeGate.Enabled;
        if (RadialSpikeZonesCheckBox != null)
            RadialSpikeZonesCheckBox.IsChecked = filters.SpikeGate.RadialZonesEnabled;
        if (CenterZoneSlider != null)
            CenterZoneSlider.Value = filters.SpikeGate.CenterZoneEnd;
        if (CenterSpikeSensitivitySlider != null)
            CenterSpikeSensitivitySlider.Value = filters.SpikeGate.CenterDeltaThreshold;
        if (CenterSmoothingMultiplierSlider != null)
            CenterSmoothingMultiplierSlider.Value = Math.Clamp(filters.SpikeGate.CenterSmoothingMultiplier, 0.5, 3.0);
        if (ZImpulseGuardEnabledCheckBox != null)
            ZImpulseGuardEnabledCheckBox.IsChecked = filters.ZImpulseGuard.Enabled;
        if (ZImpulseCenterRadiusSlider != null)
            ZImpulseCenterRadiusSlider.Value = Math.Clamp(filters.ZImpulseGuard.CenterRadius, 0.10, 0.60);
        if (ZImpulseThresholdSlider != null)
            ZImpulseThresholdSlider.Value = Math.Clamp(filters.ZImpulseGuard.IntentThreshold, 0.02, 0.20);
        if (ZImpulseConfirmSlider != null)
            ZImpulseConfirmSlider.Value = Math.Clamp(filters.ZImpulseGuard.ConfirmSamples, 1, 5);
        if (AxisIntentEnabledCheckBox != null)
            AxisIntentEnabledCheckBox.IsChecked = filters.AxisIntent.Enabled;
        if (AxisIntentDeflectionSlider != null)
            AxisIntentDeflectionSlider.Value = Math.Clamp(filters.AxisIntent.DeflectionThreshold, 0.03, 0.30);
        if (AxisIntentStrongSlider != null)
            AxisIntentStrongSlider.Value = Math.Clamp(filters.AxisIntent.StrongDeflectionThreshold, 0.10, 0.60);
        if (AxisIntentConfirmSlider != null)
            AxisIntentConfirmSlider.Value = Math.Clamp(filters.AxisIntent.ConfirmSamples, 1, 5);
        if (CrossAxisShieldEnabledCheckBox != null)
            CrossAxisShieldEnabledCheckBox.IsChecked = filters.CrossAxisShield.Enabled;
        if (CrossAxisTargetComboBox != null)
            CrossAxisTargetComboBox.SelectedValue = string.IsNullOrWhiteSpace(filters.CrossAxisShield.TargetAxisId)
                ? "SELECTED"
                : filters.CrossAxisShield.TargetAxisId.ToUpperInvariant();
        var watched = filters.CrossAxisShield.WatchedAxes ?? [];
        if (CrossWatchXCheckBox != null) CrossWatchXCheckBox.IsChecked = watched.Any(a => a.Equals("X", StringComparison.OrdinalIgnoreCase));
        if (CrossWatchYCheckBox != null) CrossWatchYCheckBox.IsChecked = watched.Any(a => a.Equals("Y", StringComparison.OrdinalIgnoreCase));
        if (CrossWatchZCheckBox != null) CrossWatchZCheckBox.IsChecked = watched.Any(a => a.Equals("Z", StringComparison.OrdinalIgnoreCase));
        if (CrossWatchRXCheckBox != null) CrossWatchRXCheckBox.IsChecked = watched.Any(a => a.Equals("RX", StringComparison.OrdinalIgnoreCase));
        if (CrossWatchRYCheckBox != null) CrossWatchRYCheckBox.IsChecked = watched.Any(a => a.Equals("RY", StringComparison.OrdinalIgnoreCase));
        if (CrossWatchRZCheckBox != null) CrossWatchRZCheckBox.IsChecked = watched.Any(a => a.Equals("RZ", StringComparison.OrdinalIgnoreCase));
        if (CrossAxisDeflectionSlider != null)
            CrossAxisDeflectionSlider.Value = Math.Clamp(filters.CrossAxisShield.OtherAxisDeflectionThreshold, 0.02, 0.50);
        if (CrossAxisDominanceCheckBox != null)
            CrossAxisDominanceCheckBox.IsChecked = filters.CrossAxisShield.RequireOtherAxisDominance;
        if (CrossAxisDominanceSlider != null)
            CrossAxisDominanceSlider.Value = Math.Clamp(filters.CrossAxisShield.OtherAxisDominanceRatio, 0.80, 2.50);
        if (CrossAxisRespectIntentCheckBox != null)
            CrossAxisRespectIntentCheckBox.IsChecked = filters.CrossAxisShield.RespectTargetIntent;
        if (CrossAxisVelocityMinSlider != null)
            CrossAxisVelocityMinSlider.Value = Math.Clamp(filters.CrossAxisShield.MinOtherAxisVelocityPerSecond, 0.2, 20.0);
        if (CrossAxisVelocityMaxSlider != null)
            CrossAxisVelocityMaxSlider.Value = Math.Clamp(filters.CrossAxisShield.MaxOtherAxisVelocityPerSecond, 2.0, 60.0);
        if (CrossAxisRateMulSlider != null)
            CrossAxisRateMulSlider.Value = Math.Clamp(filters.CrossAxisShield.RateLimitMultiplierWhenActive, 0.1, 1.0);
        if (CrossAxisEmaMulSlider != null)
            CrossAxisEmaMulSlider.Value = Math.Clamp(filters.CrossAxisShield.EmaAlphaMultiplierWhenActive, 0.1, 1.0);
        if (CrossAxisHardLockCheckBox != null)
            CrossAxisHardLockCheckBox.IsChecked = filters.CrossAxisShield.HardLockWhenActive;
        if (CrossAxisHardLockCenterCheckBox != null)
            CrossAxisHardLockCenterCheckBox.IsChecked = filters.CrossAxisShield.HardLockForceCenter;
        if (CrossAxisLeakSlider != null)
            CrossAxisLeakSlider.Value = Math.Clamp(filters.CrossAxisShield.LockLeakMultiplier, 0.0, 0.35);

        if (AxisBindLockEnabledCheckBox != null)
            AxisBindLockEnabledCheckBox.IsChecked = filters.AxisBindLock.Enabled;
        _pipeline.Settings.AxisBindLock = FilterSettingsSnapshot.Clone(new FilterSettings
        {
            AxisBindLock = filters.AxisBindLock
        }).AxisBindLock;
        UpdateAxisBindLockStatusText();

        if (filters.SpikeGate.RadialZonesEnabled)
        {
            SpikeDeltaSlider.Value = filters.SpikeGate.OuterDeltaThreshold;
            SpikeVelocitySlider.Value = filters.SpikeGate.OuterVelocityThresholdPerSecond;
            SpikeHoldSlider.Value = Math.Clamp(filters.SpikeGate.OuterMaxConsecutiveSuppressions, 1, 6);
        }
        else
        {
            SpikeDeltaSlider.Value = filters.SpikeGate.DeltaThreshold;
            SpikeVelocitySlider.Value = filters.SpikeGate.VelocityThresholdPerSecond;
            SpikeHoldSlider.Value = Math.Clamp(filters.SpikeGate.MaxConsecutiveSuppressions, 1, 6);
        }

        RateLimiterEnabledCheckBox.IsChecked = filters.RateLimiter.Enabled;
        RateLimiterSlider.Value = filters.RateLimiter.MaxDeltaPerSecond;
        EmaEnabledCheckBox.IsChecked = filters.Ema.Enabled;
        EmaAlphaSlider.Value = filters.Ema.Alpha;

        SyncFilterUiText();
        SyncFilterPipelineFromUi();
    }

    private TunerProfile BuildProfileFromUi()
    {
        var selectedDevice = DeviceComboBox.SelectedItem as InputDeviceInfo;
        var selectedAxis = AxisComboBox.SelectedItem as InputAxisInfo;

        return new TunerProfile
        {
            Name = string.IsNullOrWhiteSpace(ProfileNameTextBox.Text) ? "Default" : ProfileNameTextBox.Text.Trim(),
            DeviceId = selectedDevice?.Id ?? string.Empty,
            DeviceName = selectedDevice?.DisplayName ?? string.Empty,
            AxisName = selectedAxis?.Id ?? "RZ",
            PollingHz = (int)PollingSlider.Value,
            OutputSink = GetSelectedOutputSinkKey(),
            VJoyDeviceId = Math.Clamp(_preferences.VJoyDeviceId, 1, 16),
            Calibration = new CalibrationSettings
            {
                Min = CalibrationMinSlider.Value,
                Center = CalibrationCenterSlider.Value,
                Max = CalibrationMaxSlider.Value
            },
            Filters = CaptureFilterSettingsFromUi()
        };
    }

    private async void RefreshDevicesButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshDevicesAsync();
    }

    private async void DeviceComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await RefreshAxesAsync();
        ScheduleFilterSessionSave();
    }

    private void PollingSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PollingValueTextBlock != null)
            PollingValueTextBlock.Text = ((int)PollingSlider.Value).ToString();
        ScheduleFilterSessionSave();
    }

    private void ProfileNameTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (ProfileTitleTextBlock != null)
            UpdateProfileTitle();
    }

    private void OutputSinkComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OutputSinkComboBox.SelectedItem is not IOutputSink sink)
            return;

        if (!sink.IsAvailable)
        {
            SetStatus("status.outputUnavailable", sink.Name);
            return;
        }

        StatusTextBlock.Text = $"{T("label.output")}: {sink.Name}";
        _preferences.OutputSink = GetSelectedOutputSinkKey();
        ScheduleFilterSessionSave();
    }

    private async void DetectAxisButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DeviceComboBox.SelectedValue is not string deviceId || string.IsNullOrWhiteSpace(deviceId))
        {
            SetStatus("status.selectDevice");
            return;
        }

        try
        {
            DetectAxisButton.IsEnabled = false;
            SetStatus("status.detectAxisStart");
            _diagnostics.LogAppEvent("detect-axis-start", $"device={deviceId}");
            var axis = await _inputProvider
                .DetectMostActiveAxisAsync(deviceId, sampleDurationMs: 2200, pollingHz: (int)PollingSlider.Value)
                .ConfigureAwait(true);

            if (axis == null)
            {
                SetStatus("status.detectAxisFail");
                _diagnostics.LogAppEvent("detect-axis-fail", $"device={deviceId}");
                return;
            }

            var index = _axes.FindIndex(item => item.Id == axis.Id);
            if (index >= 0)
                AxisComboBox.SelectedIndex = index;
            SetStatus("status.detectAxisDone", axis.DisplayName);
            _diagnostics.LogAppEvent("detect-axis-done", $"device={deviceId}; axis={axis.Id}; display={axis.DisplayName}");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = ex.Message;
            _diagnostics.LogAppEvent("detect-axis-error", ex.Message);
        }
        finally
        {
            DetectAxisButton.IsEnabled = true;
        }
    }

    private void CalibrationSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsFilterControlsReady())
            return;

        if (CalibrationCenterSlider.Value <= CalibrationMinSlider.Value)
            CalibrationCenterSlider.Value = CalibrationMinSlider.Value + 0.01;
        if (CalibrationCenterSlider.Value >= CalibrationMaxSlider.Value)
            CalibrationCenterSlider.Value = CalibrationMaxSlider.Value - 0.01;
        SyncFilterUiText();
        ScheduleFilterSessionSave();
    }

    private void FilterSettingChanged(object sender, RoutedEventArgs e)
    {
        OnUserFilterChanged();
        if (sender == AxisBindLockEnabledCheckBox)
            RefreshBindLockPollingIfRunning();
    }

    private void FilterSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        SyncFilterUiText();
        OnUserFilterChanged();
    }

    private async void StartButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DeviceComboBox.SelectedValue is not string deviceId || string.IsNullOrWhiteSpace(deviceId))
        {
            SetStatus("status.selectDevice");
            return;
        }

        if (AxisComboBox.SelectedValue is not string axisId || string.IsNullOrWhiteSpace(axisId))
        {
            SetStatus("status.selectAxis");
            return;
        }

        await TryStartStreamAsync().ConfigureAwait(true);
    }

    private async void StopButton_OnClick(object sender, RoutedEventArgs e)
    {
        _isRunning = false;
        _streamAxisId = string.Empty;
        _inputProvider.Stop();
        await EndActiveOutputSessionAsync().ConfigureAwait(true);
        _activeOutputSink = null;
        _lastAxesSnapshot = null;
        _lastAxesSnapshotTimestamp = default;
        _axisIntentTracker.Reset();
        _crossAxisLockSmoother.Reset();
        _axisBindLockToggleLatched = false;
        _axisBindLockPreviousPressed = false;
        _axisBindLockActive = false;
        _crossAxisActivityLatched = false;
        _otherAxisSustainedSamples = 0;
        _lastPollTimestamp = default;
        _saturatedRawSamples = 0;
        _saturationWarned = false;
        SetStatus("status.stopped");
        _diagnostics.LogAppEvent("stream-stop", "manual stop");
        await SaveFilterSessionAsync(force: true).ConfigureAwait(true);
    }

    private async void SaveProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON profile (*.json)|*.json",
                AddExtension = true,
                DefaultExt = ".json",
                FileName = $"{ProfileNameTextBox.Text.Trim()}.json"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            var profile = BuildProfileFromUi();
            await _profileStore.SaveAsync(dialog.FileName, profile).ConfigureAwait(true);
            await SaveFilterSessionAsync(force: true).ConfigureAwait(true);
            SetStatus("status.profileSaved", dialog.FileName);
            _diagnostics.LogAppEvent("profile-save", dialog.FileName);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = ex.Message;
            _diagnostics.LogAppEvent("profile-save-error", ex.Message);
        }
    }

    private async void LoadProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON profile (*.json)|*.json"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            var profile = await _profileStore.LoadAsync(dialog.FileName).ConfigureAwait(true);
            _profile = profile;
            LoadProfileIntoUi(profile);
            SetStatus("status.profileLoaded", dialog.FileName);
            _diagnostics.LogAppEvent("profile-load", dialog.FileName);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = ex.Message;
            _diagnostics.LogAppEvent("profile-load-error", ex.Message);
        }
    }

    private void AddProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var name = $"{ProfileNameTextBox.Text.Trim()}_{DateTime.Now:HHmmss}";
        ProfilesListBox.Items.Add(new ListBoxItem { Content = name });
        ProfilesListBox.SelectedIndex = ProfilesListBox.Items.Count - 1;
    }

    private void DeleteProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (ProfilesListBox.Items.Count <= 1)
        {
            SetStatus("status.profileDeleteBlocked");
            return;
        }

        var selectedIndex = ProfilesListBox.SelectedIndex;
        if (selectedIndex < 0)
            return;

        var removedName = (ProfilesListBox.Items[selectedIndex] as ListBoxItem)?.Content?.ToString() ?? string.Empty;
        ProfilesListBox.Items.RemoveAt(selectedIndex);
        ProfilesListBox.SelectedIndex = Math.Min(selectedIndex, ProfilesListBox.Items.Count - 1);

        var path = GetProfileFilePath(removedName);
        if (File.Exists(path))
            File.Delete(path);

        SetStatus("status.profileDeleted");
        _diagnostics.LogAppEvent("profile-delete", $"index={selectedIndex}");
    }

    private async void ApplySettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ApplySettingsWindow(ProfileNameTextBox.Text.Trim())
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
            return;

        var profileName = dialog.EnteredProfileName;
        ProfileNameTextBox.Text = profileName;
        UpdateProfileTitle();

        var profile = BuildProfileFromUi();
        profile.Name = profileName;
        _profile = profile;

        var filePath = GetProfileFilePath(profileName);
        await _profileStore.SaveAsync(filePath, profile).ConfigureAwait(true);
        EnsureProfileInList(profileName);

        _preferences.AppliedProfileName = profileName;
        _preferences.HasAppliedSettings = true;
        _preferences.AutoApplyInApp = true;
        _preferences.StartWithWindows = dialog.StartWithWindows;
        if (DeviceComboBox.SelectedValue is string appliedDeviceId)
            _preferences.LastStreamDeviceId = appliedDeviceId;
        if (AxisComboBox.SelectedValue is string appliedAxisId)
            _preferences.LastStreamAxisId = appliedAxisId;
        _preferences.LastStreamPollingHz = Math.Max(20, (int)PollingSlider.Value);
        _preferences.OutputSink = GetSelectedOutputSinkKey();
        _preferences.LoggingEnabled = EnableLoggingCheckBox.IsChecked == true;
        _preferences.ShowFilterHints = ShowHintsCheckBox.IsChecked == true;
        _diagnostics.Enabled = _preferences.LoggingEnabled;

        try
        {
            _startupService.SetEnabled(
                _preferences.StartWithWindows,
                GetExecutablePathForStartup(),
                "--agent");
            _updatingUiFromPreferences = true;
            StartWithWindowsCheckBox.IsChecked = _preferences.StartWithWindows;
            _updatingUiFromPreferences = false;
            await _preferencesStore.SaveAsync(_preferences).ConfigureAwait(true);
            SetStatus("status.applied", profileName);
            await SaveFilterSessionAsync(force: true).ConfigureAwait(true);
            _ = Dispatcher.BeginInvoke(() =>
            {
                SetStatus(_preferences.StartWithWindows ? "status.startupAgentOn" : "status.startupAgentOff");
            }, DispatcherPriority.Background);
            _diagnostics.LogAppEvent("apply-settings", $"profile={profileName}; startup={_preferences.StartWithWindows}");
        }
        catch (Exception ex)
        {
            SetStatus("status.startupAgentError", ex.Message);
            _diagnostics.LogAppEvent("apply-settings-error", ex.Message);
        }
    }

    private async void SettingsCheckBoxChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingUiFromPreferences)
            return;

        _preferences.LoggingEnabled = EnableLoggingCheckBox.IsChecked == true;
        _preferences.ResetLogOnStartup = ResetLogOnStartupCheckBox?.IsChecked == true;
        _preferences.ShowFilterHints = ShowHintsCheckBox.IsChecked == true;
        _preferences.AutoApplyInApp = AutoApplyInAppCheckBox.IsChecked == true;
        var startupEnabled = StartWithWindowsCheckBox.IsChecked == true;
        _diagnostics.Enabled = _preferences.LoggingEnabled;
        ApplyHintsVisibility();

        if (_preferences.StartWithWindows != startupEnabled)
        {
            try
            {
                _startupService.SetEnabled(startupEnabled, GetExecutablePathForStartup(), "--agent");
                _preferences.StartWithWindows = startupEnabled;
                SetStatus(startupEnabled ? "status.startupAgentOn" : "status.startupAgentOff");
            }
            catch (Exception ex)
            {
                _updatingUiFromPreferences = true;
                StartWithWindowsCheckBox.IsChecked = _preferences.StartWithWindows;
                _updatingUiFromPreferences = false;
                SetStatus("status.startupAgentError", ex.Message);
                _diagnostics.LogAppEvent("startup-toggle-error", ex.Message);
                return;
            }
        }

        await _preferencesStore.SaveAsync(_preferences).ConfigureAwait(true);
        ScheduleFilterSessionSave();
    }

    private void ClearLogButton_OnClick(object sender, RoutedEventArgs e)
    {
        _diagnostics.ResetLogFile("manual");
        _diagnostics.LogAppEvent("log-cleared", $"path={_diagnostics.LogFilePath}");
        SetStatus("status.logReset", _diagnostics.LogFilePath);
    }

    private async void ResetAppliedButton_OnClick(object sender, RoutedEventArgs e)
    {
        _preferences.AppliedProfileName = string.Empty;
        _preferences.HasAppliedSettings = false;
        _preferences.AgentResumeStream = false;
        _preferences.AutoApplyInApp = false;
        _preferences.StartWithWindows = false;
        _startupService.SetEnabled(false, string.Empty, string.Empty);
        AutoApplyInAppCheckBox.IsChecked = false;
        StartWithWindowsCheckBox.IsChecked = false;
        await _preferencesStore.SaveAsync(_preferences).ConfigureAwait(true);
        SetStatus("status.resetApplied");
        _diagnostics.LogAppEvent("reset-applied-settings", "manual reset");
    }

    private async void DefaultSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var selectedDevice = DeviceComboBox.SelectedItem as InputDeviceInfo;
            var selectedAxis = AxisComboBox.SelectedItem as InputAxisInfo;
            var currentProfileName = string.IsNullOrWhiteSpace(ProfileNameTextBox.Text) ? "Default" : ProfileNameTextBox.Text.Trim();

            var defaults = new TunerProfile
            {
                Name = currentProfileName,
                DeviceId = selectedDevice?.Id ?? _profile.DeviceId,
                DeviceName = selectedDevice?.DisplayName ?? _profile.DeviceName,
                AxisName = selectedAxis?.Id ?? _profile.AxisName
            };

            _profile = defaults;
            LoadProfileIntoUi(defaults);
            ProfileNameTextBox.Text = currentProfileName;
            UpdateProfileTitle();

            _preferences.LoggingEnabled = false;
            _preferences.ShowFilterHints = true;
            _preferences.AutoApplyInApp = false;
            _preferences.StartWithWindows = false;
            _preferences.OutputSink = "vJoy";
            _preferences.VJoyDeviceId = 1;
            _preferences.AppliedProfileName = currentProfileName;
            _preferences.UiLanguage = _language;
            _diagnostics.Enabled = false;

            _startupService.SetEnabled(false, string.Empty, string.Empty);
            SelectOutputSink(_vJoySink.IsAvailable ? "vJoy" : "Monitor");
            _vJoySink.Configure(1);
            ApplyPreferencesToUi();
            ApplyLanguageFromPreferences();
            await _preferencesStore.SaveAsync(_preferences).ConfigureAwait(true);
            SetStatus("status.defaultsApplied");
            _diagnostics.LogAppEvent("defaults-applied", $"profile={currentProfileName}");
        }
        catch (Exception ex)
        {
            SetStatus("status.startupAgentError", ex.Message);
            _diagnostics.LogAppEvent("defaults-apply-error", ex.Message);
        }
    }

    private async void ProfilesListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileNameTextBox == null || ProfileTitleTextBlock == null)
            return;

        if (ProfilesListBox.SelectedItem is ListBoxItem item)
        {
            var profileName = item.Content?.ToString() ?? "Default";
            ProfileNameTextBox.Text = profileName;
            UpdateProfileTitle();

            var path = GetProfileFilePath(profileName);
            if (File.Exists(path))
            {
                try
                {
                    var profile = await _profileStore.LoadAsync(path).ConfigureAwait(true);
                    _profile = profile;
                    LoadProfileIntoUi(profile);
                    if (!string.IsNullOrWhiteSpace(profile.DeviceId))
                    {
                        DeviceComboBox.SelectedValue = profile.DeviceId;
                        await RefreshAxesAsync().ConfigureAwait(true);
                        if (!string.IsNullOrWhiteSpace(profile.AxisName))
                            AxisComboBox.SelectedValue = NormalizeAxisId(profile.AxisName);
                    }

                    _filterSessionDirty = true;
                    ScheduleFilterSessionSave();
                }
                catch
                {
                    // Ignore malformed profile and keep current UI state.
                }
            }
        }
    }

    private async void LanguageComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!AreLanguageControlsReady() || _updatingUiFromPreferences)
            return;

        if (LanguageComboBox.SelectedItem is ComboBoxItem item)
        {
            _language = (item.Content?.ToString() ?? "RU").ToUpperInvariant();
            _preferences.UiLanguage = _language;
            ApplyLanguage();
            LogCurrentSettings();
            await _preferencesStore.SaveAsync(_preferences).ConfigureAwait(true);
            ScheduleFilterSessionSave();
        }
    }

    private void ChartCanvas_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        BindMonitorChartAxes();
        if (ChartCanvas?.Parent is FrameworkElement parent)
        {
            var w = parent.ActualWidth;
            var h = parent.ActualHeight;
            if (w > 10 && h > 10)
            {
                ChartCanvas.Width = w;
                ChartCanvas.Height = h;
            }
        }

        RenderChart();
    }

    private void ApplyPreferencesToUi()
    {
        _updatingUiFromPreferences = true;
        EnableLoggingCheckBox.IsChecked = _preferences.LoggingEnabled;
        if (ResetLogOnStartupCheckBox != null)
            ResetLogOnStartupCheckBox.IsChecked = _preferences.ResetLogOnStartup;
        ShowHintsCheckBox.IsChecked = _preferences.ShowFilterHints;
        AutoApplyInAppCheckBox.IsChecked = _preferences.AutoApplyInApp;
        StartWithWindowsCheckBox.IsChecked = _preferences.StartWithWindows;
        _updatingUiFromPreferences = false;
        ApplyHintsVisibility();
    }

    private void ApplyLanguageFromPreferences()
    {
        if (!AreLanguageControlsReady())
            return;

        var lang = string.IsNullOrWhiteSpace(_preferences.UiLanguage) ? "RU" : _preferences.UiLanguage;
        _language = lang.ToUpperInvariant();

        _updatingUiFromPreferences = true;
        foreach (ComboBoxItem item in LanguageComboBox.Items)
        {
            if (!string.Equals(item.Content?.ToString(), _language, StringComparison.OrdinalIgnoreCase))
                continue;

            LanguageComboBox.SelectedItem = item;
            break;
        }

        _updatingUiFromPreferences = false;
        ApplyLanguage();
    }

    private static string GetExecutablePathForStartup()
        => AgentLauncher.GetHostExecutablePath();

    private void ApplyHintsVisibility()
    {
        var visibility = _preferences.ShowFilterHints ? Visibility.Visible : Visibility.Collapsed;
        DeadzoneHintTextBlock.Visibility = visibility;
        MedianHampelHintTextBlock.Visibility = visibility;
        SpikeHintTextBlock.Visibility = visibility;
        RateLimiterHintTextBlock.Visibility = visibility;
        EmaHintTextBlock.Visibility = visibility;
    }

    private string GetProfileFilePath(string profileName)
    {
        var safeName = string.IsNullOrWhiteSpace(profileName)
            ? "Default"
            : string.Join("_", profileName.Split(IOPath.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "Default";
        return IOPath.Combine(_profilesRoot, $"{safeName}.json");
    }

    private void LoadProfilesListFromDisk()
    {
        ProfilesListBox.Items.Clear();
        var files = Directory.GetFiles(_profilesRoot, "*.json")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
        {
            ProfilesListBox.Items.Add(new ListBoxItem { Content = "Default" });
            ProfilesListBox.SelectedIndex = 0;
            return;
        }

        foreach (var file in files)
            ProfilesListBox.Items.Add(new ListBoxItem { Content = IOPath.GetFileNameWithoutExtension(file) });

        var preferred = string.IsNullOrWhiteSpace(_preferences.AppliedProfileName) ? "Default" : _preferences.AppliedProfileName;
        var index = ProfilesListBox.Items.Cast<ListBoxItem>().ToList().FindIndex(item => string.Equals(item.Content?.ToString(), preferred, StringComparison.OrdinalIgnoreCase));
        ProfilesListBox.SelectedIndex = index >= 0 ? index : 0;
    }

    private void EnsureProfileInList(string profileName)
    {
        var item = ProfilesListBox.Items.Cast<ListBoxItem>()
            .FirstOrDefault(x => string.Equals(x.Content?.ToString(), profileName, StringComparison.OrdinalIgnoreCase));
        if (item == null)
            ProfilesListBox.Items.Add(new ListBoxItem { Content = profileName });

        var idx = ProfilesListBox.Items.Cast<ListBoxItem>()
            .ToList()
            .FindIndex(x => string.Equals(x.Content?.ToString(), profileName, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
            ProfilesListBox.SelectedIndex = idx;
    }

    private async Task AutoApplyProfileAsync(bool startStream)
    {
        var profileName = GetCommittedAppliedProfileName();
        if (string.IsNullOrWhiteSpace(profileName))
            return;

        if (!await LoadAppliedProfileAsync(profileName).ConfigureAwait(true))
            return;

        if (startStream && !_isRunning)
            await TryStartStreamAsync().ConfigureAwait(true);
    }

    private void UpdateProfileTitle()
    {
        var name = string.IsNullOrWhiteSpace(ProfileNameTextBox?.Text) ? "Default" : ProfileNameTextBox.Text.Trim();
        ProfileTitleTextBlock.Text = string.Format(T("profile.title"), name);
    }

    private void LogCurrentSettings()
    {
        var deviceId = DeviceComboBox.SelectedValue?.ToString() ?? string.Empty;
        var axisId = AxisComboBox.SelectedValue?.ToString() ?? string.Empty;
        _diagnostics.LogSettings(
            _pipeline.Settings,
            (int)PollingSlider.Value,
            _language,
            deviceId,
            axisId,
            GetCurrentProfileName());
    }

    private string GetCurrentProfileName() =>
        string.IsNullOrWhiteSpace(ProfileNameTextBox?.Text) ? "Default" : ProfileNameTextBox.Text.Trim();

    private void ScheduleFilterSessionSave()
    {
        if (_suppressFilterSessionSave)
            return;

        _filterSaveTimer?.Stop();
        _filterSaveTimer?.Start();
    }

    private async Task SaveFilterSessionAsync(bool force = false)
    {
        if (!force && !_filterSessionDirty)
            return;

        try
        {
            _filterSaveTimer?.Stop();
            var session = CaptureSessionFromUi();
            await _filterSessionStore.SaveAsync(session).ConfigureAwait(true);
            _filterSessionDirty = false;
            _diagnostics.LogAppEvent(
                "filters-saved",
                $"path={_filterSessionStore.FilePath}; forced={(force ? 1 : 0)}; device={session.DeviceId}; axis={session.AxisId}");
        }
        catch (Exception ex)
        {
            _diagnostics.LogAppEvent("filters-save-error", ex.Message);
        }
    }

    private async Task<bool> TryRestoreFilterSessionAsync()
    {
        try
        {
            var session = await _filterSessionStore.LoadAsync().ConfigureAwait(true);
            if (session == null || !session.UserModified)
                return false;

            _filterSaveTimer?.Stop();
            _suppressFilterSessionSave = true;
            try
            {
                await ApplySessionToUiAsync(session).ConfigureAwait(true);
            }
            finally
            {
                _suppressFilterSessionSave = false;
            }

            _filterSessionDirty = false;
            _diagnostics.LogAppEvent(
                "filters-loaded",
                $"path={_filterSessionStore.FilePath}; savedAt={session.SavedAt:O}; profile={session.ProfileName}; device={session.DeviceId}; axis={session.AxisId}");
            SetStatus("status.filtersRestored");
            return true;
        }
        catch (Exception ex)
        {
            _diagnostics.LogAppEvent("filters-load-error", ex.Message);
            return false;
        }
    }

    private bool HasCommittedAppliedProfile()
    {
        if (string.IsNullOrWhiteSpace(_preferences.AppliedProfileName))
            return false;

        if (!_preferences.HasAppliedSettings)
        {
            // Legacy appsettings: AppliedProfileName without flag still counts if profile file exists.
            return File.Exists(GetProfileFilePath(_preferences.AppliedProfileName));
        }

        return true;
    }

    private string? GetCommittedAppliedProfileName()
    {
        if (!HasCommittedAppliedProfile())
            return null;

        return _preferences.AppliedProfileName.Trim();
    }

    private bool ShouldRunBackgroundStreamWorkflow()
    {
        if (_isRunning)
            return false;

        if (App.IsAgentMode || _preferences.AgentResumeStream)
            return HasCommittedAppliedProfile();

        return _preferences.AutoApplyInApp && HasCommittedAppliedProfile();
    }

    private async Task TryRunBackgroundStreamWorkflowAsync()
    {
        if (!ShouldRunBackgroundStreamWorkflow())
            return;

        if (App.AgentStartupDelayMs > 0)
            await Task.Delay(App.AgentStartupDelayMs).ConfigureAwait(true);

        var profileName = GetCommittedAppliedProfileName();
        if (string.IsNullOrWhiteSpace(profileName))
        {
            _diagnostics.LogAppEvent("agent-workflow", "skip:no-applied-profile");
            return;
        }

        if (!File.Exists(GetProfileFilePath(profileName)))
        {
            _diagnostics.LogAppEvent("agent-workflow", $"skip:profile-missing; name={profileName}");
            return;
        }

        _diagnostics.LogAppEvent(
            "agent-workflow",
            $"mode={(App.IsAgentMode ? "agent" : "auto")}; appliedProfile={profileName}; handoff={_preferences.AgentResumeStream}");

        var maxAttempts = App.IsAgentMode || _preferences.AgentResumeStream ? 45 : 1;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (!await LoadAppliedProfileAsync(profileName).ConfigureAwait(true))
                {
                    _diagnostics.LogAppEvent("agent-workflow", $"profile-load-failed; name={profileName}");
                    return;
                }

                await TryRestoreFilterSessionAsync().ConfigureAwait(true);

                var sinkKey = string.IsNullOrWhiteSpace(_preferences.OutputSink) ? "vJoy" : _preferences.OutputSink;
                SelectOutputSink(sinkKey);
                _vJoySink.Configure((uint)Math.Clamp(_preferences.VJoyDeviceId, 1, 16));
                VJoyOutputSink.TryReleaseStale((uint)Math.Clamp(_preferences.VJoyDeviceId, 1, 16));

                if (!await TrySelectStreamDeviceAndAxisAsync().ConfigureAwait(true))
                {
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(2000).ConfigureAwait(true);
                        continue;
                    }

                    _diagnostics.LogAppEvent("agent-workflow", "device-axis-unavailable");
                    return;
                }

                if (PollingSlider != null && _preferences.LastStreamPollingHz >= 20)
                    PollingSlider.Value = _preferences.LastStreamPollingHz;

                if (await TryStartStreamAsync().ConfigureAwait(true))
                {
                    if (_preferences.AgentResumeStream)
                    {
                        _preferences.AgentResumeStream = false;
                        await _preferencesStore.SaveAsync(_preferences).ConfigureAwait(true);
                    }

                    _diagnostics.LogAppEvent("agent-workflow", $"stream-running; attempt={attempt}");
                    return;
                }

                if (attempt < maxAttempts)
                    await Task.Delay(2000).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _diagnostics.LogAppEvent("agent-workflow-error", $"attempt={attempt}; {ex.Message}");
                if (attempt >= maxAttempts)
                    break;

                await Task.Delay(2000).ConfigureAwait(true);
            }
        }
    }

    private async Task<bool> LoadAppliedProfileAsync(string profileName)
    {
        var path = GetProfileFilePath(profileName);
        if (!File.Exists(path))
            return false;

        var profile = await _profileStore.LoadAsync(path).ConfigureAwait(true);
        _profile = profile;
        LoadProfileIntoUi(profile);

        if (!string.IsNullOrWhiteSpace(profile.DeviceId))
            DeviceComboBox.SelectedValue = profile.DeviceId;
        else if (_devices.Count > 0 && DeviceComboBox.SelectedIndex < 0)
            DeviceComboBox.SelectedIndex = 0;

        await RefreshAxesAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(profile.AxisName))
            AxisComboBox.SelectedValue = NormalizeAxisId(profile.AxisName);

        return true;
    }

    private async Task<bool> TrySelectStreamDeviceAndAxisAsync()
    {
        await RefreshDevicesAsync().ConfigureAwait(true);

        if (!string.IsNullOrWhiteSpace(_preferences.LastStreamDeviceId))
            DeviceComboBox.SelectedValue = _preferences.LastStreamDeviceId;

        var deviceId = DeviceComboBox.SelectedValue as string;
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            if (_devices.Count > 0)
                DeviceComboBox.SelectedIndex = 0;
            deviceId = DeviceComboBox.SelectedValue as string;
        }

        if (string.IsNullOrWhiteSpace(deviceId))
            return false;

        await RefreshAxesAsync().ConfigureAwait(true);

        var preferredAxis = NormalizeAxisId(_preferences.LastStreamAxisId);
        if (!string.IsNullOrWhiteSpace(preferredAxis))
            AxisComboBox.SelectedValue = preferredAxis;

        if (AxisComboBox.SelectedValue is not string selectedAxis || string.IsNullOrWhiteSpace(selectedAxis))
        {
            if (!string.IsNullOrWhiteSpace(_profile.AxisName))
                AxisComboBox.SelectedValue = NormalizeAxisId(_profile.AxisName);
            else if (_axes.Count > 0)
                AxisComboBox.SelectedIndex = 0;
        }

        return AxisComboBox.SelectedValue is string axis && !string.IsNullOrWhiteSpace(axis);
    }

    private async Task<bool> TryStartStreamAsync()
    {
        if (DeviceComboBox.SelectedValue is not string deviceId || string.IsNullOrWhiteSpace(deviceId))
        {
            SetStatus("status.selectDevice");
            return false;
        }

        if (AxisComboBox.SelectedValue is not string axisId || string.IsNullOrWhiteSpace(axisId))
        {
            SetStatus("status.selectAxis");
            return false;
        }

        try
        {
            SyncFilterPipelineFromUi();
            _profile = BuildProfileFromUi();
            _pipeline.Reset();
            _lastAxesSnapshot = null;
            _lastAxesSnapshotTimestamp = default;
            _axisIntentTracker.Reset();
            _crossAxisLockSmoother.Reset();
            _crossAxisActivityLatched = false;
            _otherAxisSustainedSamples = 0;
            _lastPollTimestamp = default;
            _lastLoggedSpikeCount = 0;
            _lastLoggedHampelCount = 0;
            _saturatedRawSamples = 0;
            _saturationWarned = false;

            lock (_historySync)
            {
                _rawHistory.Clear();
                _filteredHistory.Clear();
                _monitorChartAxes.Clear();
            }

            var sink = GetSelectedOutputSink();
            if (sink == null)
            {
                SetStatus("status.selectDevice");
                return false;
            }

            if (!sink.IsAvailable)
            {
                if (sink is VJoyOutputSink)
                    SetStatus("status.vjoyMissing");
                else
                    SetStatus("status.outputUnavailable", sink.Name);
                return false;
            }

            if (sink is VJoyOutputSink vJoySink)
                vJoySink.Configure((uint)Math.Clamp(_preferences.VJoyDeviceId, 1, 16));

            _activeOutputSink = sink;
            await sink.BeginSessionAsync(axisId).ConfigureAwait(true);

            _streamAxisId = axisId;
            UpdateChartLegend();
            LogMonitorChartAxesSelection("stream-start");
            _streamPollingHz = Math.Max(20, (int)PollingSlider.Value);
            _isRunning = true;
            _inputProvider.Start(deviceId, axisId, _streamPollingHz, CreateBindLockPollConfig());
            await SaveStreamSessionToPreferencesAsync().ConfigureAwait(true);
            SetStatus("status.running", deviceId, axisId);
            if (sink is VJoyOutputSink)
                SetStatus("status.vjoyActive", _preferences.VJoyDeviceId);

            _diagnostics.LogAppEvent(
                "stream-start",
                $"device={deviceId}; axis={axisId}; hz={(int)PollingSlider.Value}; sink={GetSelectedOutputSinkKey()}; vjoyDevice={_preferences.VJoyDeviceId}; agent={App.IsAgentMode}");
            if (sink is VJoyOutputSink activeVJoy && activeVJoy.LastError != null)
                _diagnostics.LogAppEvent("vjoy-warning", activeVJoy.LastError);
            LogCurrentSettings();
            return true;
        }
        catch (Exception ex)
        {
            _isRunning = false;
            _activeOutputSink = null;
            await EndActiveOutputSessionAsync().ConfigureAwait(true);
            if (ex.Message.Contains("used by another application", StringComparison.OrdinalIgnoreCase))
                SetStatus("status.vjoyBusy", _preferences.VJoyDeviceId);
            else
                StatusTextBlock.Text = ex.Message;
            _diagnostics.LogAppEvent("stream-start-error", ex.Message);
            return false;
        }
    }

    private async Task SaveStreamSessionToPreferencesAsync()
    {
        if (DeviceComboBox.SelectedValue is string deviceId)
            _preferences.LastStreamDeviceId = deviceId;
        if (AxisComboBox.SelectedValue is string axisId)
            _preferences.LastStreamAxisId = axisId;
        _preferences.LastStreamPollingHz = Math.Max(20, (int)PollingSlider.Value);
        _preferences.OutputSink = GetSelectedOutputSinkKey();
        await _preferencesStore.SaveAsync(_preferences).ConfigureAwait(true);
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeAfterHandoff || App.IsAgentMode || !_isRunning || _handoffInProgress)
            return;

        if (!HasCommittedAppliedProfile())
        {
            _diagnostics.LogAppEvent("agent-handoff", "skip:no-applied-profile");
            return;
        }

        e.Cancel = true;
        ShowInTaskbar = false;
        Hide();
        _ = PerformHandoffThenCloseAsync();
    }

    private async Task PerformHandoffThenCloseAsync()
    {
        if (_handoffInProgress)
            return;

        _handoffInProgress = true;
        ShowInTaskbar = false;
        Hide();
        try
        {
            if (IsFilterControlsReady())
                SyncFilterPipelineFromUi();

            await SaveFilterSessionAsync(force: true).ConfigureAwait(true);
            if (!await SaveHandoffPreferencesAsync().ConfigureAwait(true))
            {
                FinishHandoffShutdown();
                return;
            }

            AgentLauncher.LaunchDetached(delayMs: 1500);
            _agentSpawnedForHandoff = true;
            _diagnostics.LogAppEvent(
                "agent-handoff",
                $"spawned; appliedProfile={_preferences.AppliedProfileName}");

            await Task.Delay(250).ConfigureAwait(true);

            FinishHandoffShutdown();
        }
        catch (Exception ex)
        {
            _diagnostics.LogAppEvent("agent-handoff-error", ex.Message);
            FinishHandoffShutdown();
        }
        finally
        {
            _handoffInProgress = false;
        }
    }

    private void FinishHandoffShutdown()
    {
        _closeAfterHandoff = true;
        Application.Current.Shutdown();
    }

    private async Task<bool> SaveHandoffPreferencesAsync()
    {
        if (DeviceComboBox.SelectedValue is string deviceId)
            _preferences.LastStreamDeviceId = deviceId;
        if (AxisComboBox.SelectedValue is string axisId)
            _preferences.LastStreamAxisId = axisId;
        _preferences.LastStreamPollingHz = Math.Max(20, _streamPollingHz);
        _preferences.OutputSink = GetSelectedOutputSinkKey();
        _preferences.AgentResumeStream = true;

        try
        {
            await _preferencesStore.SaveAsync(_preferences).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex)
        {
            _diagnostics.LogAppEvent("agent-handoff-error", $"prefs: {ex.Message}");
            return false;
        }
    }

    protected override async void OnClosed(EventArgs e)
    {
        var wasRunning = _isRunning;

        if (!_agentSpawnedForHandoff && wasRunning && !App.IsAgentMode)
        {
            if (IsFilterControlsReady())
                SyncFilterPipelineFromUi();
            await SaveFilterSessionAsync(force: true).ConfigureAwait(true);
            if (HasCommittedAppliedProfile() && await SaveHandoffPreferencesAsync().ConfigureAwait(true))
            {
                try
                {
                    AgentLauncher.LaunchDetached(delayMs: 1500);
                    _agentSpawnedForHandoff = true;
                    _diagnostics.LogAppEvent("agent-handoff", "spawned from OnClosed fallback");
                }
                catch (Exception ex)
                {
                    _diagnostics.LogAppEvent("agent-handoff-error", ex.Message);
                }
            }
        }

        _isRunning = false;
        if (wasRunning)
            _inputProvider.Stop();
        await EndActiveOutputSessionAsync().ConfigureAwait(true);
        _activeOutputSink = null;
        _inputProvider.ReadingAvailable -= InputProviderOnReadingAvailable;
        _inputProvider.Dispose();

        _diagnostics.LogAppEvent(
            "app-close",
            wasRunning
                ? $"window closed; handoff={(_agentSpawnedForHandoff ? 1 : 0)}; profile={_preferences.AppliedProfileName}"
                : "window closed");
        base.OnClosed(e);
    }

    private static string NormalizeAxisId(string? axisName)
    {
        if (string.IsNullOrWhiteSpace(axisName))
            return "RZ";

        return axisName.Trim().ToUpperInvariant() switch
        {
            "YAW" => "RZ",
            "ROTATION Z" => "RZ",
            "ROTATION Z (YAW COMMON)" => "RZ",
            _ => axisName.Trim().ToUpperInvariant()
        };
    }

    private IOutputSink? GetSelectedOutputSink()
        => OutputSinkComboBox?.SelectedItem as IOutputSink;

    private string GetSelectedOutputSinkKey()
    {
        var selected = GetSelectedOutputSink();
        if (selected == null)
            return "Monitor";

        foreach (var pair in _outputSinks)
        {
            if (ReferenceEquals(pair.Value, selected))
                return pair.Key;
        }

        return "Monitor";
    }

    private void SelectOutputSink(string key)
    {
        if (OutputSinkComboBox == null)
            return;

        if (!_outputSinks.TryGetValue(key, out var sink))
            sink = _outputSinks["Monitor"];

        OutputSinkComboBox.SelectedItem = sink;
    }

    private async Task EndActiveOutputSessionAsync()
    {
        var sink = _activeOutputSink ?? GetSelectedOutputSink();
        if (sink == null)
            return;

        try
        {
            await sink.EndSessionAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _diagnostics.LogAppEvent("output-end-error", ex.Message);
        }
    }

    private bool IsFilterControlsReady()
    {
        return DeadzoneRadiusSlider != null &&
               DeadzoneRadiusTextBlock != null &&
               MedianWindowSlider != null &&
               MedianWindowTextBlock != null &&
               HampelWindowSlider != null &&
               HampelWindowTextBlock != null &&
               HampelSigmaSlider != null &&
               HampelSigmaTextBlock != null &&
               SpikeDeltaSlider != null &&
               SpikeDeltaTextBlock != null &&
               SpikeVelocitySlider != null &&
               SpikeVelocityTextBlock != null &&
               SpikeHoldSlider != null &&
               SpikeHoldTextBlock != null &&
               ZImpulseGuardEnabledCheckBox != null &&
               ZImpulseCenterRadiusSlider != null &&
               ZImpulseCenterRadiusTextBlock != null &&
               ZImpulseThresholdSlider != null &&
               ZImpulseThresholdTextBlock != null &&
               ZImpulseConfirmSlider != null &&
               ZImpulseConfirmTextBlock != null &&
               CrossAxisShieldEnabledCheckBox != null &&
               CrossAxisTargetComboBox != null &&
               CrossWatchXCheckBox != null &&
               CrossWatchYCheckBox != null &&
               CrossWatchZCheckBox != null &&
               CrossWatchRXCheckBox != null &&
               CrossWatchRYCheckBox != null &&
               CrossWatchRZCheckBox != null &&
               CrossAxisDeflectionSlider != null &&
               CrossAxisDeflectionTextBlock != null &&
               CrossAxisDominanceCheckBox != null &&
               CrossAxisDominanceSlider != null &&
               CrossAxisDominanceTextBlock != null &&
               CrossAxisRespectIntentCheckBox != null &&
               AxisIntentEnabledCheckBox != null &&
               AxisIntentDeflectionSlider != null &&
               AxisIntentDeflectionTextBlock != null &&
               AxisIntentStrongSlider != null &&
               AxisIntentStrongTextBlock != null &&
               AxisIntentConfirmSlider != null &&
               AxisIntentConfirmTextBlock != null &&
               CrossAxisVelocityMinSlider != null &&
               CrossAxisVelocityMinTextBlock != null &&
               CrossAxisVelocityMaxSlider != null &&
               CrossAxisVelocityMaxTextBlock != null &&
               CrossAxisRateMulSlider != null &&
               CrossAxisRateMulTextBlock != null &&
               CrossAxisEmaMulSlider != null &&
               CrossAxisEmaMulTextBlock != null &&
               CrossAxisHardLockCheckBox != null &&
               CrossAxisHardLockCenterCheckBox != null &&
               CrossAxisLeakSlider != null &&
               CrossAxisLeakTextBlock != null &&
               DeadzoneHintTextBlock != null &&
               MedianHampelHintTextBlock != null &&
               RateLimiterHintTextBlock != null &&
               EmaHintTextBlock != null &&
               RateLimiterSlider != null &&
               RateLimiterTextBlock != null &&
               EmaAlphaSlider != null &&
               EmaAlphaTextBlock != null &&
               CalibrationMinSlider != null &&
               CalibrationCenterSlider != null &&
               CalibrationMaxSlider != null &&
               CalibrationMinTextBlock != null &&
               CalibrationCenterTextBlock != null &&
               CalibrationMaxTextBlock != null &&
               DeadzoneEnabledCheckBox != null &&
               DeadzoneDynamicCheckBox != null &&
               MedianEnabledCheckBox != null &&
               HampelEnabledCheckBox != null &&
               SpikeGateEnabledCheckBox != null &&
               RateLimiterEnabledCheckBox != null &&
               EmaEnabledCheckBox != null;
    }

    private bool AreLanguageControlsReady()
    {
        return AppSubtitleTextBlock != null &&
               AppVersionTextBlock != null &&
               ProfilesHeaderTextBlock != null &&
               LanguageLabelTextBlock != null &&
               InstructionHeaderTextBlock != null &&
               InstructionTextBlock != null &&
               AddProfileButton != null &&
               DeleteProfileButton != null &&
               RefreshDevicesButton != null &&
               StartButton != null &&
               StopButton != null &&
               DetectAxisButton != null &&
               SaveProfileButton != null &&
               LoadProfileButton != null &&
               ApplySettingsButton != null &&
               EnableLoggingCheckBox != null &&
               ResetLogOnStartupCheckBox != null &&
               ClearLogButton != null &&
               ShowHintsCheckBox != null &&
               AutoApplyInAppCheckBox != null &&
               StartWithWindowsCheckBox != null &&
               ResetAppliedButton != null &&
               DefaultSettingsButton != null &&
               SettingsHeaderTextBlock != null &&
               MovementHintTextBlock != null &&
               SpikeHoldLabelTextBlock != null &&
               SpikeHintTextBlock != null &&
               ZImpulseGuardHeaderTextBlock != null &&
               ZImpulseGuardHintTextBlock != null &&
               AxisIntentHeaderTextBlock != null &&
               AxisIntentHintTextBlock != null &&
               AxisIntentEnabledCheckBox != null &&
               AxisIntentDeflectionSlider != null &&
               AxisIntentDeflectionTextBlock != null &&
               AxisIntentStrongSlider != null &&
               AxisIntentStrongTextBlock != null &&
               AxisIntentConfirmSlider != null &&
               AxisIntentConfirmTextBlock != null &&
               CrossAxisShieldHeaderTextBlock != null &&
               CrossAxisShieldHintTextBlock != null &&
               CrossAxisTargetLabelTextBlock != null &&
               CrossAxisWatchLabelTextBlock != null &&
               CrossAxisDeflectionLabelTextBlock != null &&
               CrossAxisDominanceCheckBox != null &&
               CrossAxisDominanceSlider != null &&
               CrossAxisDominanceTextBlock != null &&
               CrossAxisRespectIntentCheckBox != null &&
               CrossAxisVelocityMinLabelTextBlock != null &&
               CrossAxisVelocityMaxLabelTextBlock != null &&
               CrossAxisRateMulLabelTextBlock != null &&
               CrossAxisEmaMulLabelTextBlock != null &&
               CrossAxisLeakLabelTextBlock != null &&
               CrossAxisHardLockCheckBox != null &&
               CrossAxisHardLockCenterCheckBox != null &&
               InputTab != null &&
               FiltersTab != null &&
               MonitorTab != null &&
               SettingsTab != null &&
               StatusTextBlock != null;
    }
}

internal static class CollectionExtensions
{
    public static int FindIndex<T>(this IEnumerable<T> source, Func<T, bool> predicate)
    {
        var index = 0;
        foreach (var item in source)
        {
            if (predicate(item))
                return index;
            index++;
        }

        return -1;
    }
}
