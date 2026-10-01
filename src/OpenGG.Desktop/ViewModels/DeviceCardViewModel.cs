using OpenGG.Desktop.Mvvm;

namespace OpenGG.Desktop.ViewModels;

public sealed class DeviceCardViewModel(KeyboardDevice device) : ObservableObject
{
    private bool _connected;
    public KeyboardDevice Device { get; private set; } = device;
    public string Id => Device.Id;
    public override string ToString() => Name;
    public string Name => Device.Name;
    public string Vendor => "SteelSeries";
    public string Category => Device.HasVerifiedReceiver ? "Hall effect keyboard + OLED" : "Keyboard · diagnostics";
    public string PortraitId => Device.ProductId == 0x1644 ? "kbd-steelseries-apex-pro-tkl-gen3" : "drawing:keyboard";
    public bool IsConnected { get => _connected; private set { if (Set(ref _connected, value)) { OnPropertyChanged(nameof(StatusLabel)); OnPropertyChanged(nameof(StatusKey)); } } }
    public string StatusLabel => IsConnected ? "Connected" : "Disconnected";
    public string StatusKey => IsConnected ? "Brush.Status.Online" : "Brush.Status.Offline";
    public bool HasMetric => HasBattery;
    public bool HasBattery => IsConnected && BatteryPercent is not null;
    public string? Metric => HasBattery ? $"{BatteryPercent}%" : null;
    public string? MetricCaption => HasBattery ? IsCharging ? BatteryPercent >= 100 ? "Charged" : "Charging" : "Battery" : null;
    private int? _batteryPercent;
    private bool _isCharging;
    public int? BatteryPercent => _batteryPercent;
    public bool IsCharging => _isCharging;
    public string BatteryText => HasBattery ? $"{BatteryPercent}% · {MetricCaption}" : "Battery unavailable";
    public void UpdateBattery(int? percent, bool charging)
    {
        if (_batteryPercent == percent && _isCharging == charging) return;
        _batteryPercent = percent is { } value ? Math.Clamp(value, 0, 100) : null;
        _isCharging = percent is not null && charging;
        foreach (var name in new[] { nameof(BatteryPercent), nameof(IsCharging), nameof(HasBattery), nameof(HasMetric), nameof(Metric), nameof(MetricCaption), nameof(BatteryText) }) OnPropertyChanged(name);
    }
    public void Update(KeyboardDevice? live)
    {
        IsConnected = live is not null;
        if (!IsConnected) UpdateBattery(null, false);
        if (live is not null) { Device = live; OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(Category)); }
    }
}
