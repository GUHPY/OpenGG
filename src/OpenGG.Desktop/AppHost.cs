using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OneRGB.Application;
using OneRGB.Application.Control;
using OneRGB.Application.Devices.Input;
using OneRGB.Application.Devices.Keyboard;
using OneRGB.Application.Profiles;
using OneRGB.Application.Lighting;
using OneRGB.Domain;
using OneRGB.Hardware.Integrations;
using OneRGB.Hardware.Native;
using OneRGB.Windows;

namespace OpenGG.Desktop;

public sealed record OpenGGSettings
{
    public IReadOnlyList<Macro> Macros { get; init; } = [];
    public IReadOnlyList<KeyboardDevice> Remembered { get; init; } = [];
    public LightingScene Lighting { get; init; } = new(LightingMode.Wave, new Rgb(6,182,212),1,.5);
    public bool RgbControlled { get; init; } = true;
    public bool RgbGlow { get; init; } = true;
}

/// <summary>Keyboard services only. Storage and hardware ownership are independent of OneRGB.</summary>
public sealed class AppHost : IDisposable
{
    public static string DataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenGG");
    public static JsonSerializerOptions Json { get; } = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private IReadOnlyList<HidInterfaceInfo> _interfaces = [];
    private IReadOnlyList<string> _externalOwners = [];
    private readonly SemaphoreSlim _scan = new(1, 1);
    public ControlArbiter Arbiter { get; } = new();
    public OperationJournal Journal { get; } = new();
    public ControlService Controls { get; }
    public ProfileLibrary ProfileLibrary { get; }
    public ProfileManager Profiles { get; }
    public ApexHidLink Link { get; }
    public ApexKeyboardIntegration ApexKeyboard { get; }
    public OpenGGSettings Settings { get; private set; }
    public IReadOnlyList<KeyboardDevice> Keyboards { get; private set; } = [];
    public IReadOnlyList<HidInterfaceInfo> Interfaces => _interfaces;
    public bool Scanning { get; private set; }
    public event EventHandler? ScanCompleted;
    public event EventHandler? VendorAppsChanged;
    public event EventHandler? SettingsChanged;
    public event EventHandler<CommandResult>? CommandCompleted;
    public static readonly KnownDevice Apex = new(AnalogKeyboardDescriptor.ApexProTklGen3.DeviceId,
        "SteelSeries Apex Pro TKL Wireless Gen 3", "Keyboard", "USB / 2.4 GHz",
        [new ExpectedHardwareId("USB\\VID_1038&PID_1644", VerificationStatus.VerifiedOnHardware,
            "Tested on firmware 3.24.1, schema 19, 1038:1644 MI_03 FFC0:0001", new UsbIdentity(0x1038,0x1644,3,0xFFC0,1))],
        ["SteelSeries GG"], ["Actuation", "Rapid Trigger", "Onboard profiles", "RGB", "OLED"],
        "Advanced writes require the exact verified receiver, a loaded profile and exclusive ownership.", 1);

    public AppHost()
    {
        Directory.CreateDirectory(DataDirectory);
        Settings = Read<OpenGGSettings>("settings.json") ?? new();
        ProfileLibrary = new ProfileLibrary(Read<Profile[]>("profiles.json") ?? []);
        Controls = new ControlService(Arbiter, Journal);
        Profiles = new ProfileManager(Controls, ProfileLibrary);
        ProfileLibrary.Changed += (_, _) => Save("profiles.json", ProfileLibrary.All);
        Link = new ApexHidLink(() => _interfaces);
        ApexKeyboard = new ApexKeyboardIntegration(Arbiter, [Apex], () => _externalOwners.Count != 0, Link, () => _interfaces);
        Controls.Executed += (_, result) => CommandCompleted?.Invoke(this, result);
        Journal.Changed += (_, entry) =>
        {
            if (entry.Completed) { File.AppendAllText(Path.Combine(DataDirectory, "operations.jsonl"), JsonSerializer.Serialize(entry, new JsonSerializerOptions(Json) { WriteIndented = false }) + Environment.NewLine); }
        };
        foreach (var control in AnalogKeyboardDescriptor.ApexProTklGen3.Controls().Where(c => !ApexLive.Writes(c.Key.Setting)))
        {
            Controls.Register(new StoredSetting(control.Key, control.Spec with { StoredOnly = true, Readable = false }, this));
        }
    }

    public IReadOnlyList<string> ExternalOwnersOf(IEnumerable<string> ids)
        => _externalOwners;

    private static IReadOnlyList<string> FindExternalOwners()
    {
        List<string> names = [];
        foreach (var name in new[] { "SteelSeriesEngine", "SteelSeriesPrism", "OneRGB", "OpenRGB", "SignalRgb" })
        {
            var processes = Process.GetProcessesByName(name);
            if (processes.Length > 0) { names.Add(name == "SteelSeriesEngine" ? "SteelSeries GG" : name); }
            foreach (var process in processes) { process.Dispose(); }
        }
        return names;
    }

    public async Task ScanAsync()
    {
        if (!await _scan.WaitAsync(0).ConfigureAwait(false)) { return; }
        Scanning = true;
        try
        {
            _externalOwners = FindExternalOwners();
            if (_externalOwners.Count != 0) { Link.Drop(); }
            _interfaces = await Task.Run(() => HidEnumerator.Enumerate("vid_1038")).ConfigureAwait(false);
            Keyboards = SteelSeriesKeyboards.Discover(_interfaces);
            var verified = Keyboards.Where(k => k.HasVerifiedReceiver).ToList();
            // ponytail: one verified receiver at a time; add receiver selection when multi-keyboard writes are validated.
            if (verified.Count > 1) { _interfaces = [.. _interfaces.Where(i => i.Identity.ProductId != 0x1644)]; }
            var discovery = await ApexKeyboard.DiscoverAsync(CancellationToken.None).ConfigureAwait(false);
            var present = discovery.Adapters.Select(a => a.Key).ToHashSet();
            foreach (var status in Controls.Statuses().Where(s => ApexKeyboardIntegration.Claims(s.Key) && !present.Contains(s.Key))) { Controls.Disconnect(status.Key); }
            foreach (var adapter in discovery.Adapters) { Controls.Register(adapter); }
            var remembered = Settings.Remembered.ToDictionary(k => k.Id);
            foreach (var keyboard in Keyboards) { remembered[keyboard.Id] = keyboard with { Interfaces = [] }; }
            var next = remembered.Values.OrderBy(k => k.Id, StringComparer.Ordinal).ToList();
            if (!Settings.Remembered.OrderBy(k => k.Id, StringComparer.Ordinal).Select(k => (k.Id,k.Name)).SequenceEqual(next.Select(k => (k.Id,k.Name))))
            { Update(s => s with { Remembered = next }); }
            VendorAppsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { Scanning = false; _scan.Release(); ScanCompleted?.Invoke(this, EventArgs.Empty); }
    }

    public void Update(Func<OpenGGSettings, OpenGGSettings> change)
    {
        lock (_scan)
        {
            Settings = change(Settings);
            Save("settings.json", Settings);
        }
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static T? Read<T>(string name)
    {
        var path = Path.Combine(DataDirectory, name);
        if (!File.Exists(path)) { return default; }
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json); }
        catch (JsonException) { File.Copy(path, path + ".invalid-" + DateTimeOffset.UtcNow.ToUnixTimeSeconds(), true); return default; }
    }

    private static void Save<T>(string name, T value)
    {
        var path = Path.Combine(DataDirectory, name);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, Json));
        File.Move(path + ".tmp", path, true);
    }

    public void Dispose() { Profiles.Dispose(); Controls.Dispose(); ApexKeyboard.Dispose(); Link.Dispose(); _scan.Dispose(); }

    private sealed class StoredSetting(ControlKey key, ControlSpec spec, AppHost host) : IControlAdapter
    {
        public ControlKey Key => key;
        public ControlSpec Spec => spec;
        public bool IsAvailable => host.Keyboards.Any(k => k.HasVerifiedReceiver);
        public bool WriteEnabled => true;
        public string? WriteDisabledReason => null;
        public Task<ControlValue?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult<ControlValue?>(null);
        public Task WriteAsync(ControlValue value, Lease lease, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    }
}
