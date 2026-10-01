using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using OpenGG.Desktop.Mvvm;
using OneRGB.Application.Control;
using OneRGB.Application.Devices.Keyboard;

namespace OpenGG.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppHost _host;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _poll;
    private readonly DispatcherTimer _batteryPoll;
    private readonly CancellationTokenSource _stop = new();
    private bool _readingBattery, _disposed;
    private string _filter = "all";
    private string _status = "Scanning for SteelSeries keyboards…";
    private string? _error;
    private string _diagnostic = string.Empty;
    private bool _diagnosing;
    private DeviceCardViewModel? _selected;
    private string _section = "devices";
    private Task<string>? _featureProbe;
    private IReadOnlyList<DiagnosticTool> _tools = [];
    private bool _installingTool;
    private string _toolInstallStatus = string.Empty;

    public MainViewModel(AppHost host, Dispatcher dispatcher)
    {
        _host = host; _dispatcher = dispatcher;
        foreach (var remembered in host.Settings.Remembered) { Cards.Add(new DeviceCardViewModel(remembered)); }
        View = CollectionViewSource.GetDefaultView(Cards);
        View.Filter = value => value is DeviceCardViewModel card
            && (Filter == "all" || (Filter == "online" ? card.IsConnected : !card.IsConnected));
        ScanCommand = new RelayCommand(async () => await ScanManuallyAsync());
        OpenDeviceCommand = new RelayCommand(p => { if (p is DeviceCardViewModel card) { Selected = card; Diagnostic = string.Empty; DeviceRequested?.Invoke(this, card); } });
        BackCommand = new RelayCommand(() => { Selected = null; DeviceRequested?.Invoke(this, null); });
        DiagnoseCommand = new RelayCommand(async () => await DiagnoseAsync(), () => !_diagnosing && Selected?.IsConnected == true);
        ExportDiagnosticCommand = new RelayCommand(ExportDiagnostic, () => !string.IsNullOrWhiteSpace(Diagnostic));
        DismissErrorCommand = new RelayCommand(() => Error = null);
        OpenDiagnosticsCommand = new RelayCommand(() => Section = "diagnostics");
        ExportInventoryCommand = new RelayCommand(async () => await ExportInventoryAsync());
        OpenToolLinkCommand = new RelayCommand(OpenToolDocumentation);
        InstallToolCommand = new RelayCommand(async p => { if (p is DiagnosticTool tool) { await InstallToolAsync(tool); } }, p => !_installingTool && p is DiagnosticTool { CanInstall: true });
        _host.ScanCompleted += (_, _) => _dispatcher.BeginInvoke(ApplyScan);
        _host.CommandCompleted += (_, result) =>
        {
            if (result.Outcome == CommandOutcome.Failed) { _dispatcher.BeginInvoke(() => Error = "Failed to apply: " + result.Message); }
        };
        _poll = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, async (_, _) => await ScanAsync(), dispatcher);
        _poll.Start();
        _batteryPoll = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, async (_, _) => await RefreshBatteryAsync(), dispatcher);
        _batteryPoll.Start();
    }

    public bool Expert => false;
    private bool _isScanning;
    public bool IsScanning { get => _isScanning; private set => Set(ref _isScanning,value); }
    public string Section
    {
        get => _section;
        set
        {
            if (!Set(ref _section,value)) { return; }
            if (value == "devices") { Selected = null; }
            else { Selected ??= Cards.FirstOrDefault(c => c.IsConnected) ?? Cards.FirstOrDefault(); }
            DeviceRequested?.Invoke(this,null);
            OnPropertyChanged(nameof(IsDevices)); OnPropertyChanged(nameof(IsDiagnostics)); OnPropertyChanged(nameof(IsHome)); OnPropertyChanged(nameof(HasDetail)); OnPropertyChanged(nameof(PageTitle));
            if (IsDiagnostics) { RefreshTools(); }
        }
    }
    public bool IsDevices => Section == "devices";
    public bool IsDiagnostics => Section == "diagnostics";
    public bool HasDetail => IsDevices && Selected is not null;
    public string PageTitle => IsDiagnostics ? "Keyboard diagnostics" : SelectedName;
    public bool TestLiveWrites { get; set; }
    public IReadOnlyList<DiagnosticTool> Tools { get => _tools; private set => Set(ref _tools,value); }
    public string ToolInstallStatus { get => _toolInstallStatus; private set => Set(ref _toolInstallStatus,value); }
    public event EventHandler<DeviceCardViewModel?>? DeviceRequested;
    public ObservableCollection<DeviceCardViewModel> Cards { get; } = [];
    public ICollectionView View { get; }
    public string Filter { get => _filter; set { if (Set(ref _filter, value)) { Refresh(); } } }
    public bool IsEmpty => View.IsEmpty;
    public bool HasDevices => Cards.Count > 0;
    public string Status { get => _status; set => Set(ref _status, value); }
    public string? Error { get => _error; set => Set(ref _error, value); }
    public string Diagnostic { get => _diagnostic; private set => Set(ref _diagnostic, value); }
    public DeviceCardViewModel? Selected { get => _selected; set { if (Set(ref _selected, value)) { OnPropertyChanged(nameof(IsHome)); OnPropertyChanged(nameof(SelectedName)); OnPropertyChanged(nameof(HasDetail)); OnPropertyChanged(nameof(PageTitle)); } } }
    public bool IsHome => IsDevices && Selected is null;
    public string SelectedName => Selected?.Name ?? "Devices";
    public RelayCommand ScanCommand { get; }
    public RelayCommand OpenDeviceCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand DiagnoseCommand { get; }
    public RelayCommand ExportDiagnosticCommand { get; }
    public RelayCommand DismissErrorCommand { get; }
    public RelayCommand OpenDiagnosticsCommand { get; }
    public RelayCommand ExportInventoryCommand { get; }
    public RelayCommand OpenToolLinkCommand { get; }
    public RelayCommand InstallToolCommand { get; }

    private void OpenToolDocumentation(object? parameter)
    {
        if (parameter is not string url) { return; }
        try
        {
            if (Uri.TryCreate(url,UriKind.Absolute,out var uri) && uri.Scheme == "https")
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            else if (url is "docs/diagnostics.md" or "docs/protocol.md" or "docs/reproduce.md")
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(DiagnosticTools.Root,url)) { UseShellExecute = true });
        }
        catch (Exception ex) { Error = "Could not open documentation: " + ex.Message; }
    }

    private async Task InstallToolAsync(DiagnosticTool tool)
    {
        if (_installingTool || !tool.CanInstall) { return; }
        _installingTool = true;
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        try
        {
            var root = DiagnosticTools.Root;
            if (tool.Id is "hid-inventory" or "etw-capture")
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { ArgumentList = { Path.Combine(root,"tools") }, UseShellExecute = true });
                ToolInstallStatus = "Files are included in tools; see Documentation for instructions.";
                return;
            }
            if (tool.Id is not ("python" or "frida" or "wireshark" or "usbpcap" or "usbview" or "protocol-cli" or "capture-analyzer")) { throw new ArgumentException("Unknown tool."); }
            ToolInstallStatus = $"{tool.Name}: checking local files and preparing installation…";
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = root,
                ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(root,"tools","Install-ResearchTool.ps1"), "-Tool", tool.Id }
            };
            // PowerShell 7 launch environments can hide Windows PowerShell's Get-FileHash module.
            start.Environment.Remove("PSModulePath");
            using var process = System.Diagnostics.Process.Start(start) ?? throw new IOException("Could not start installation.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stdout = await output; var stderr = await error;
            Directory.CreateDirectory(AppHost.DataDirectory);
            await File.AppendAllTextAsync(Path.Combine(AppHost.DataDirectory,"tool-install.log"),$"{DateTimeOffset.UtcNow:o} {tool.Id} exit={process.ExitCode}\n{stdout}\n{stderr}\n");
            if (process.ExitCode != 0) { throw new IOException(string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim()); }
            ToolInstallStatus = $"{tool.Name}: installation completed. Files and output are recorded in the local diagnostic log.";
        }
        catch (Exception ex) { ToolInstallStatus = $"{tool.Name}: installation failed. " + ex.Message; }
        finally { _installingTool = false; RefreshTools(); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    private void RefreshTools()
    {
        try { Tools = DiagnosticTools.Inventory(); }
        catch (System.Security.SecurityException ex) { Error = "Tool inventory: " + ex.Message; }
    }

    public Task TakeControlAsync(string name, IEnumerable<string> ids)
    {
        Error = "Close " + string.Join(", ", _host.ExternalOwnersOf(ids)) + " before controlling the keyboard in OpenGG.";
        return Task.CompletedTask;
    }

    public async Task ScanAsync()
    {
        try { await _host.ScanAsync(); }
        catch (Exception ex) { Error = "Could not scan for devices: " + ex.Message; }
    }

    public async Task ScanManuallyAsync()
    {
        if (IsScanning) { return; }
        IsScanning = true;
        try { await Task.WhenAll(ScanAsync(),Task.Delay(400)); }
        finally { IsScanning = false; }
    }

    private void ApplyScan()
    {
        foreach (var live in _host.Keyboards)
        {
            if (Cards.All(c => c.Id != live.Id)) { Cards.Add(new DeviceCardViewModel(live)); }
        }
        foreach (var card in Cards) { card.Update(_host.Keyboards.FirstOrDefault(k => k.Id == card.Id)); }
        Status = $"{Cards.Count(c => c.IsConnected)} connected · SteelSeries keyboards only";
        Refresh();
        if (Selected is { } selected) { OnPropertyChanged(nameof(SelectedName)); }
        _ = RefreshBatteryAsync();
    }

    public async Task RefreshBatteryAsync()
    {
        if (_disposed || _readingBattery) return;
        var active = OneRGB.Hardware.Lighting.ApexLighting.Find(_host.ControlInterfaces)?.DevicePath;
        var verified = Cards.Where(c => c.IsConnected && c.Device.HasVerifiedTransport && c.Device.Interfaces.Any(i => i.DevicePath == active)).ToArray();
        foreach (var card in Cards.Except(verified)) card.UpdateBattery(null, false);
        if (verified.Length != 1 || _host.ExternalOwnersOf([]).Count != 0)
        {
            foreach (var card in verified) card.UpdateBattery(null, false);
            return;
        }
        _readingBattery = true;
        try
        {
            var resource = AnalogKeyboardDescriptor.ApexProTklGen3.SettingsResource;
            var percent = await _host.Controls.RefreshAsync(new ControlKey(resource, "battery"), _stop.Token);
            var charging = percent is NumberValue ? await _host.Controls.RefreshAsync(new ControlKey(resource, "battery.charging"), _stop.Token) : null;
                if (!_disposed && verified[0].IsConnected && _host.ExternalOwnersOf([]).Count == 0 && OneRGB.Hardware.Lighting.ApexLighting.Find(_host.ControlInterfaces)?.DevicePath == active)
                verified[0].UpdateBattery(percent is NumberValue number && charging is ToggleValue ? (int)number.Value : null, charging is ToggleValue { Value: true });
        }
        catch (OperationCanceledException) when (_disposed) { }
        finally { _readingBattery = false; }
    }

    private void Refresh() { View.Refresh(); OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(HasDevices)); }

    private async Task DiagnoseAsync()
    {
        if (Selected is not { } card) { return; }
        _diagnosing = true;
        Diagnostic = "Testing communication…";
        var checks = new List<object>();
        RefreshTools();
        try
        {
            if (_host.ExternalOwnersOf([]).Count > 0) { throw new InvalidOperationException("Close " + string.Join(", ", _host.ExternalOwnersOf([])) + " before testing communication."); }
            await _host.ScanAsync();
            if (_host.Keyboards.FirstOrDefault(k => k.Id == card.Id) is not { } live) { throw new IOException("The keyboard disconnected."); }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            string probe, outcome;
            if (live.HasVerifiedTransport)
            {
                _host.SelectKeyboard(live);
                foreach (var opcode in live.ProductId == 0x1646 ? new byte[] { 0x92 } : [0xBC,0xD2])
                {
                    var response = await _host.Link.ReadTelemetryAsync(opcode,CancellationToken.None);
                    checks.Add(new { protocol = $"0x{opcode:X2}", response = Convert.ToHexString(response),
                        result = opcode == 0xBC ? $"Receiver connection value: {response[2]} (1 = keyboard connected)." : "Battery telemetry received; raw value retained, percentage calibration is not established." });
                    if (opcode == 0xBC && response[2] != 1) { throw new IOException("The receiver is connected but did not confirm a keyboard connection (0xBC)."); }
                }
                var blob = await _host.Link.ReadProfileAsync(2, CancellationToken.None);
                probe = live.ProductId == 0x1646 ? "0x83 direct keyboard profile read, namespace 3, 24 blocks and GetFeature" : "0x83 receiver profile read, 24 blocks, matched ACK per block and GetFeature";
                outcome = $"ACK status 0; schema 19; CRC valid; {blob.Length} bytes. No profile activated or saved.";
                checks.Add(new { protocol = "0x83", result = outcome, sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(blob)) });
                if (TestLiveWrites)
                {
                    var opcodes = await _host.ApexKeyboard.ProbeCurrentLiveAsync(CancellationToken.None);
                    checks.Add(new { protocol = string.Join(", ",opcodes.Select(o => $"0x{o:X2}")), result = "Current loaded live map re-sent; matched ACK status 0. No flash erase/write." });
                }
            }
            else
            {
                if (_featureProbe is { IsCompleted: false }) { throw new InvalidOperationException("The previous HID request is still pending in the driver."); }
                _featureProbe = Task.Run(() => KeyboardDiagnostics.ReadFeature(live));
                var report = await _featureProbe.WaitAsync(TimeSpan.FromSeconds(5));
                probe = "Read-only HID feature request, report ID 0; no Gen 3 configuration opcode sent";
                outcome = report;
                if (TestLiveWrites) { checks.Add(new { protocol = "Advanced writes", result = "Skipped: model not validated." }); }
            }
            timer.Stop();
            Diagnostic = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, device = live.Name,
                productId = $"1038:{live.ProductId:X4}", testedModel = "Apex Pro TKL Wireless Gen 3 / firmware 3.24.1 / schema 19",
                modelVerified = live.HasVerifiedTransport, probe, outcome, checks, elapsedSeconds = timer.Elapsed.TotalSeconds, tools = Tools,
                interfaces = live.Interfaces.Select(i => new { identity = i.Identity.ToString(), i.Product, i.Manufacturer, i.VersionNumber, i.ContainerId, i.InputReportLength, i.OutputReportLength, i.FeatureReportLength, i.Error }) }, AppHost.Json);
        }
        catch (Exception ex)
        {
            if (card.Device.HasVerifiedTransport) { _host.Link.Drop(); }
            Diagnostic = JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, device = card.Name, result = "Communication test failed or unavailable", error = ex.Message,
                modelVerified = card.Device.HasVerifiedTransport, checks, tools = Tools, interfaces = card.Device.Interfaces.Select(i => new { identity = i.Identity.ToString(), i.InputReportLength, i.OutputReportLength, i.FeatureReportLength, i.Error }) }, AppHost.Json);
            Error = "Diagnostic failed: " + ex.Message;
        }
        finally { _diagnosing = false; System.Windows.Input.CommandManager.InvalidateRequerySuggested(); }
    }

    private void ExportDiagnostic()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Diagnostic JSON|*.json", FileName = "opengg-diagnostic.json" };
        if (dialog.ShowDialog() == true) { File.WriteAllText(dialog.FileName, Diagnostic); Status = "Diagnostic exported."; }
    }

    private async Task ExportInventoryAsync()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "HID inventory JSON|*.json", FileName = "opengg-hid-inventory.json" };
        if (dialog.ShowDialog() != true) { return; }
        try
        {
            var interfaces = await Task.Run(() => OneRGB.Windows.HidEnumerator.Enumerate());
            var inventory = interfaces.Select(i => new { identity = i.Identity.ToString(), i.Product, i.Manufacturer, i.VersionNumber, i.ContainerId, i.InputReportLength, i.OutputReportLength, i.FeatureReportLength, i.Error });
            await File.WriteAllTextAsync(dialog.FileName,JsonSerializer.Serialize(inventory,AppHost.Json));
            Status = "Read-only HID inventory exported.";
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    public void Dispose() { _disposed = true; _poll.Stop(); _batteryPoll.Stop(); _stop.Cancel(); }
}
