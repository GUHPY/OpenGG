using System.Diagnostics;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using OpenGG.Desktop.Mvvm;
using OneRGB.Application;
using OneRGB.Application.Lighting;
using OneRGB.Application.Lighting.Spatial;
using OneRGB.Hardware.Audio;
using OneRGB.Hardware.Lighting;

namespace OpenGG.Desktop.ViewModels;

public sealed record ColorPreset(string Name, string Hex);

public sealed class ApexLightingViewModel : ObservableObject, IDisposable
{
    private readonly AppHost _host;
    private readonly ApexLighting _sink;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly EffectContext _audio = new();
    private readonly LoopbackCapture _capture;
    private LightingMode _mode = LightingMode.Wave;
    private Color _color = Color.FromRgb(0x06,0xB6,0xD4);
    private string _hex = "#06B6D4";
    private double _brightness = 100, _speed = 50;
    private bool _controlled = true, _active, _glow = true;
    private string _status = "Waiting for the keyboard.";
    private Rgb[]? _frame;
    private bool _oledSelected;
    private DeviceCardViewModel? _device;

    public ApexLightingViewModel(AppHost host, Dispatcher dispatcher, KeyboardViewModel keyboard)
    {
        _host = host;
        Keyboard = keyboard;
        keyboard.PropertyChanged += (_, _) => RaiseFeedback();
        host.VendorAppsChanged += (_, _) => dispatcher.BeginInvoke(RaiseFeedback);
        var scene = host.Settings.Lighting;
        _mode = scene.Mode; _color = Color.FromRgb(scene.Color.R,scene.Color.G,scene.Color.B);
        _hex = $"#{_color.R:X2}{_color.G:X2}{_color.B:X2}";
        _brightness = scene.Brightness * 100; _speed = scene.Speed * 100;
        _controlled = host.Settings.RgbControlled; _glow = host.Settings.RgbGlow;
        _sink = new ApexLighting(host.Arbiter, () => host.Interfaces,
            () => host.ExternalOwnersOf([]).Count == 0 && host.Keyboards.Count(k => k.HasVerifiedReceiver) == 1,
            "Use the verified receiver and close other keyboard controllers.", host.Link);
        _capture = new LoopbackCapture(_audio);
        SetModeCommand = new RelayCommand(p => { if (p is LightingMode mode) { Mode = mode; } });
        PickPresetCommand = new RelayCommand(p => { if (p is string hex) { Hex = hex; } });
        SyncCommand = new RelayCommand(async () => await host.ScanAsync());
        SetPanelCommand = new RelayCommand(p => OledSelected = p is "OLED");
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, (_, _) => Tick(), dispatcher);
    }

    public static IReadOnlyList<ColorPreset> Presets { get; } = [new("Cyber Cyan","#06B6D4"), new("Crimson Red","#EF4444"),
        new("Neon Emerald","#10B981"), new("Ultraviolet","#8B5CF6"), new("Solar Amber","#F59E0B"), new("Azure Blue","#3B82F6"), new("Pure White","#FFFFFF"), new("Deep Pink","#EC4899")];
    public LightingMode Mode { get => _mode; set { if (Set(ref _mode,value)) { OnPropertyChanged(nameof(ModeText)); } } }
    public Color Color { get => _color; set { if (Set(ref _color,value)) { _hex = $"#{value.R:X2}{value.G:X2}{value.B:X2}"; OnPropertyChanged(nameof(Hex)); OnPropertyChanged(nameof(ColorBrush)); } } }
    public string Hex
    {
        get => _hex;
        set
        {
            if (value.Trim().TrimStart('#') is { Length: 6 } text && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            { Color = Color.FromRgb((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb); }
        }
    }
    public SolidColorBrush ColorBrush { get { var brush = new SolidColorBrush(_color); brush.Freeze(); return brush; } }
    public double Brightness { get => _brightness; set { if (Set(ref _brightness,Math.Clamp(value,0,100))) { OnPropertyChanged(nameof(BrightnessRatio)); } } }
    public double Speed { get => _speed; set { if (Set(ref _speed,Math.Clamp(value,0,100))) { OnPropertyChanged(nameof(SpeedRatio)); } } }
    public double BrightnessRatio => _brightness / 100;
    public double SpeedRatio => _speed / 100;
    public bool ShowGlow { get => _glow; set => Set(ref _glow,value); }
    public bool IsActive
    {
        get => _active;
        set
        {
            if (!Set(ref _active,value)) { return; }
            if (value) { _timer.Start(); Tick(); }
            else { _timer.Stop(); _sink.Release(); _capture.Stop(); _host.Controls.Release(_sink.Resource,"opengg-rgb"); }
        }
    }
    public bool Controlled { get => _controlled; set => Set(ref _controlled,value); }
    public KeyboardViewModel Keyboard { get; }
    public DeviceCardViewModel? Device { get => _device; set { if (Set(ref _device, value)) RaiseFeedback(); } }
    public string FeedbackTitle => _sink.LastError is not null || _capture.Error is not null ? "Lighting update failed"
        : Keyboard.OnboardError is not null || Keyboard.StatusIsError ? "Keyboard update failed"
        : Keyboard.HasErrors ? "Review your changes"
        : _host.ApexKeyboard.TransportDisabledReason is not null ? "Keyboard access unavailable"
        : !Keyboard.OnboardIdle ? "Updating onboard profile"
        : Keyboard.Busy ? "Applying changes"
        : _host.ApexKeyboard.LoadedSlot != Keyboard.OnboardSlot ? "Read the profile before editing"
        : "Live controls ready";
    public string FeedbackText => _sink.LastError ?? _capture.Error ?? Keyboard.OnboardError ?? (Keyboard.StatusIsError ? Keyboard.Status : null)
        ?? (Keyboard.HasErrors ? Keyboard.Issues.First(i => i.IsError).Message : null)
        ?? _host.ApexKeyboard.TransportDisabledReason
        ?? (!Keyboard.OnboardIdle ? "Please wait for the profile operation to finish."
            : Keyboard.Busy ? "Sending the latest settings to your keyboard."
            : _host.ApexKeyboard.LoadedSlot != Keyboard.OnboardSlot ? "Choose a profile below, then click Read and activate."
            : Keyboard.Dirty ? "Changes pending. Save to keyboard stores the onboard profile."
            : "Actuation and Rapid Trigger apply live. Save to keyboard makes them permanent.");
    public string FeedbackIcon => _sink.LastError is not null || _capture.Error is not null || Keyboard.OnboardError is not null || Keyboard.StatusIsError || Keyboard.HasErrors ? "Icon.Alert"
        : _host.ApexKeyboard.LoadedSlot != Keyboard.OnboardSlot || _host.ApexKeyboard.TransportDisabledReason is not null ? "Icon.Lock" : "Icon.Check";
    public string FeedbackAccent => _sink.LastError is not null || _capture.Error is not null || Keyboard.OnboardError is not null || Keyboard.StatusIsError || Keyboard.HasErrors ? "#F07878"
        : _host.ApexKeyboard.LoadedSlot != Keyboard.OnboardSlot || _host.ApexKeyboard.TransportDisabledReason is not null ? "#E6C768" : "#71D89B";
    private void RaiseFeedback()
    {
        foreach (var name in new[] { nameof(FeedbackTitle), nameof(FeedbackText), nameof(FeedbackIcon), nameof(FeedbackAccent) }) OnPropertyChanged(name);
    }
    public bool OledSelected { get => _oledSelected; set => Set(ref _oledSelected, value); }
    public double AudioLevel => _audio.AudioLevel;
    public string StatusText { get => _status; private set { if (Set(ref _status,value)) RaiseFeedback(); } }
    public string ModeText => Mode switch { LightingMode.Wave=>"Rainbow wave", LightingMode.ColorCycle=>"Color cycle", LightingMode.Breathing=>"Breathing", LightingMode.Static=>"Static", LightingMode.AudioReactive=>"Audio reactive", _=>"Off" };
    public RelayCommand SetModeCommand { get; }
    public RelayCommand PickPresetCommand { get; }
    public RelayCommand SyncCommand { get; }
    public RelayCommand SetPanelCommand { get; }

    private void Tick()
    {
        if (!IsActive) { return; }
        var scene = new LightingScene(Mode,new Rgb(Color.R,Color.G,Color.B),BrightnessRatio,SpeedRatio);
        _frame = [.. LedMaps.Keyboard.Leds.Select(led => LightingEffects.ColorAt(scene, (int)led.X, (int)LedMaps.Keyboard.Width, _clock.Elapsed.TotalSeconds, AudioLevel))];
        OnPropertyChanged(nameof(AudioLevel));
        if (!Controlled || !_sink.IsAvailable || !_sink.WriteEnabled)
        {
            _sink.Release(); _capture.Stop();
            StatusText = !Controlled ? "RGB control is off; preview is available." : _sink.IsAvailable ? _sink.WriteDisabledReason! : "Waiting for the keyboard.";
            return;
        }
        if (Mode == LightingMode.AudioReactive) { _capture.Start(); } else { _capture.Stop(); }
        var acquired = _host.Arbiter.TryAcquire(_sink.Resource,"opengg-rgb",WriterPriority.TemporaryUserOverride);
        if (acquired.Lease is { } lease) { _sink.Submit(lease,_frame); StatusText = _capture.Error ?? _sink.LastError ?? "Streaming OpenGG colors (RGB has no ACK)."; }
        else { StatusText = "Another controller owns RGB."; }
    }

    public void Dispose()
    {
        _host.Update(s => s with { Lighting = new LightingScene(Mode,new Rgb(Color.R,Color.G,Color.B),BrightnessRatio,SpeedRatio), RgbControlled = Controlled, RgbGlow = ShowGlow });
        _timer.Stop(); _capture.Dispose(); _sink.Dispose(); _host.Controls.Release(_sink.Resource,"opengg-rgb");
    }
}
