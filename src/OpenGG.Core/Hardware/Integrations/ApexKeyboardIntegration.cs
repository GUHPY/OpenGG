using OneRGB.Application;
using OneRGB.Application.Control;
using OneRGB.Application.Devices;
using OneRGB.Application.Devices.Keyboard;
using OneRGB.Application.Integrations;
using OneRGB.Domain;
using OneRGB.Hardware.Lighting;
using OneRGB.Hardware.Native;
using OneRGB.Windows;

namespace OneRGB.Hardware.Integrations;

/// <summary>
/// Atuação e Rapid Trigger do Apex Pro TKL Gen 3 pelo mesmo HID do RGB.
/// Ao vivo: 0x6F (atuação), 0x76 (Rapid Trigger por tecla) e 0x77 (sensibilidade).
/// Perfis schema 19: leitura do receptor, backup, gravação nos dois destinos e verificação.
/// Bateria e carregando pelas consultas que o GG repete (0xBC e 0xD2 pelo receptor, 0x92 no cabo): só leitura.
/// </summary>
public sealed class ApexKeyboardIntegration : IIntegration, IDisposable
{
    private static readonly AnalogKeyboardDescriptor Keyboard = AnalogKeyboardDescriptor.ApexProTklGen3;
    private readonly ControlArbiter _arbiter;
    private readonly IReadOnlyList<KnownDevice> _catalog;
    private readonly Func<bool> _isGgRunning;
    private readonly Func<IReadOnlyList<HidInterfaceInfo>?>? _interfaces;
    private readonly ApexHidLink _link;
    private readonly Lock _stateGate = new();
    private ApexLive.State _state = new(Keyboard.DefaultActuation, Keyboard.DefaultSensitivity);
    private readonly SemaphoreSlim _io = new(1, 1);

    private List<IControlAdapter>? _adapters;
    private HidInterfaceInfo? _deviceInterface;
    private HidInterfaceInfo? _batteryInterface;
    private int? _loadedSlot;

    // A bateria e o "carregando" saem da mesma consulta: a segunda leitura seguida usa a primeira.
    private (long At, (int Percent, bool Charging)? Value)? _battery;

    public ApexKeyboardIntegration(
        ControlArbiter arbiter,
        IReadOnlyList<KnownDevice> catalog,
        Func<bool> isGgRunning,
        ApexHidLink link,
        Func<IReadOnlyList<HidInterfaceInfo>?>? interfaces = null)
    {
        _arbiter = arbiter ?? throw new ArgumentNullException(nameof(arbiter));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _isGgRunning = isGgRunning ?? throw new ArgumentNullException(nameof(isGgRunning));
        _link = link ?? throw new ArgumentNullException(nameof(link));
        _link.Faulted += InvalidateLoadedProfile;
        _interfaces = interfaces;
    }

    public IntegrationInfo Info { get; } = new(
        "apex-keyboard",
        "SteelSeries Apex Pro TKL (Actuation / Rapid Trigger)",
        "Peripherals",
        "Actuation, per-key Rapid Trigger, onboard profiles with backup and readback, OLED and battery",
        IntegrationSource.ReverseEngineered);

    public static bool Claims(ControlKey key) =>
        (key.Resource == Keyboard.ActuationResource && ApexLive.Writes(key.Setting)) || key.Resource == Keyboard.SettingsResource;

    public bool WritesAllowed =>
        _catalog.FirstOrDefault(d => d.Id == Keyboard.DeviceId)?.WritesAllowed == true;

    public bool WriteEnabled => TransportDisabledReason is null && _loadedSlot is not null;

    public string? TransportDisabledReason => !WritesAllowed
        ? "Read only: the keyboard is not verified_hw in the catalog (ADR-0005)."
        : _isGgRunning() ? "Close SteelSeries GG, OneRGB and other controllers before accessing profiles in OpenGG."
        : _deviceInterface?.Identity.ProductId is not (0x1644 or 0x1646) ? "Connect the verified 2.4 GHz receiver or USB cable for advanced settings."
        : null;

    public string? WriteDisabledReason =>
        TransportDisabledReason ?? (_loadedSlot is null ? "Choose an onboard profile and click Read and activate before editing." : null);

    public int? LoadedSlot => _loadedSlot;

    private void InvalidateLoadedProfile() => _loadedSlot = null;

    /// <summary>Re-send the known current live map. Requires an explicitly loaded profile; never erases flash.</summary>
    public async Task<int[]> ProbeCurrentLiveAsync(CancellationToken cancellationToken)
    {
        await _io.WaitAsync(cancellationToken).ConfigureAwait(false);
        Lease[] leases = [];
        try
        {
            if (_loadedSlot is null) { throw new InvalidOperationException("Click Read and activate before the temporary write test."); }
            leases = AcquireProfile();
            ApexLive.State current;
            lock (_stateGate) { current = _state.Clone(); }
            List<byte[]> reports = [ApexProtocol.ActuationFrame(current.Actuations,current.Actuations[ApexProtocol.AnalogKeys[0]]),
                ApexProtocol.ToggleFrame(0x76,current.RapidTriggers.ToDictionary(p => p.Key,p => current.Enabled && p.Value))];
            if (current.Enabled) { reports.Add(ApexProtocol.RapidTriggerFrame(current.Sensitivities,current.Sensitivities[ApexProtocol.AnalogKeys[0]])); }
            await _link.SendFeaturesAsync(reports,cancellationToken,() => CheckOwnership(leases)).ConfigureAwait(false);
            return [.. reports.Select(r => (int)ApexProtocol.CommandOpcode(_deviceInterface!.Identity.ProductId,r[1]))];
        }
        catch { _loadedSlot = null; throw; }
        finally { foreach (var lease in leases) { _arbiter.Release(lease); } _io.Release(); }
    }

    public async Task<byte[]> ReadAndLoadProfileAsync(int slot, CancellationToken cancellationToken)
    {
        await _io.WaitAsync(cancellationToken).ConfigureAwait(false);
        Lease[] leases = [];
        try
        {
            leases = AcquireProfile();
            var profile = await _link.ReadProfileAsync(slot, cancellationToken).ConfigureAwait(false);
            CheckOwnership(leases);
            await _link.LoadProfileAsync(slot, cancellationToken).ConfigureAwait(false);
            lock (_stateGate) { ApexProfile.SeedLive(profile, _state); }
            _loadedSlot = slot;
            return profile;
        }
        catch { _loadedSlot = null; throw; }
        finally
        {
            foreach (var lease in leases) { _arbiter.Release(lease); }
            _io.Release();
        }
    }

    public async Task<string> SaveProfileAsync(int slot, byte[] expectedOriginal, byte[] profile, CancellationToken cancellationToken)
    {
        ApexProfile.Validate(expectedOriginal);
        ApexProfile.Validate(profile);
        await _io.WaitAsync(cancellationToken).ConfigureAwait(false);
        Lease[] leases = [];
        try
        {
            leases = AcquireProfile();
            var current = await _link.ReadProfileAsync(slot, cancellationToken).ConfigureAwait(false);
            if (!current.AsSpan().SequenceEqual(expectedOriginal))
            {
                throw new InvalidOperationException("The profile changed after it was read. Read it again before saving.");
            }
            var backup = await _link.WriteProfileAsync(slot, profile,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenGG", "apex-backups"),
                () => CheckOwnership(leases), cancellationToken).ConfigureAwait(false);
            CheckOwnership(leases);
            await _link.LoadProfileAsync(slot, cancellationToken).ConfigureAwait(false);
            lock (_stateGate) { ApexProfile.SeedLive(profile, _state); }
            _loadedSlot = slot;
            return backup;
        }
        catch { _loadedSlot = null; throw; }
        finally
        {
            foreach (var lease in leases) { _arbiter.Release(lease); }
            _io.Release();
        }
    }

    private Lease[] AcquireProfile()
    {
        if (TransportDisabledReason is { } reason) { throw new InvalidOperationException(reason); }
        var leases = new List<Lease>();
        foreach (var resource in new[] { Keyboard.ActuationResource, Keyboard.KeysResource })
        {
            var acquired = _arbiter.TryAcquire(resource, "apex-onboard", WriterPriority.TemporaryUserOverride);
            if (acquired.Lease is not { } lease)
            {
                foreach (var owned in leases) { _arbiter.Release(owned); }
                throw new InvalidOperationException($"{resource} is controlled by {acquired.CurrentOwner}.");
            }
            leases.Add(lease);
        }
        return [.. leases];
    }

    private void CheckOwnership(IEnumerable<Lease> leases)
    {
        if (TransportDisabledReason is { } reason) { throw new InvalidOperationException(reason); }
        if (leases.Any(lease => !_arbiter.IsCurrent(lease))) { throw new InvalidOperationException("Another writer took control of the keyboard during this operation."); }
    }

    public Task<IntegrationDiscovery> DiscoverAsync(CancellationToken cancellationToken)
    {
        var interfaces = _interfaces?.Invoke() ?? HidEnumerator.Enumerate();
        var previousPath = _deviceInterface?.DevicePath;
        _deviceInterface = ApexLighting.Find(interfaces);
        if (previousPath != _deviceInterface?.DevicePath) { _loadedSlot = null; _link.Drop(); }

        // No cabo o teclado responde direto e carrega; o receptor, se ainda estiver ligado, fica em segundo.
        _batteryInterface = interfaces.Where(i => ApexLighting.Find([i]) is not null && ApexProtocol.BatteryOpcode(i.Identity.ProductId) is not null)
            .OrderBy(i => ApexProtocol.BatteryOpcode(i.Identity.ProductId) == 0x92 ? 0 : 1).FirstOrDefault();
        _battery = null;
        if (_deviceInterface is null)
        {
            return Task.FromResult(IntegrationDiscovery.None("Apex Pro TKL Gen 3 not found (2.4 GHz receiver or cable disconnected)."));
        }

        _adapters ??= BuildAdapters();
        return Task.FromResult(new IntegrationDiscovery(_adapters, [Keyboard.Name]));
    }

    public void Dispose()
    {
        _link.Faulted -= InvalidateLoadedProfile;
        _link.ClearTemporary();
        _io.Dispose();
    }

    /// <summary>Imagem 128×40 em linhas, convertida para as colunas do comando 0x4A.</summary>
    public Task SendOledAsync(byte[] bitmap, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        cancellationToken.ThrowIfCancellationRequested();
        if (TransportDisabledReason is { } reason)
        {
            throw new InvalidOperationException($"Apex OLED: {reason}");
        }

        return SendOledReportAsync(ApexProtocol.OledFrame(bitmap), cancellationToken);
    }

    public Task ResetOledAsync(CancellationToken cancellationToken) => SendOledReportAsync([0, 0x4B], cancellationToken);

    private async Task SendOledReportAsync(byte[] report, CancellationToken cancellationToken)
    {
        await _io.WaitAsync(cancellationToken).ConfigureAwait(false);
        Lease? lease = null;
        try
        {
            if (TransportDisabledReason is { } reason) { throw new InvalidOperationException(reason); }
            lease = _arbiter.TryAcquire(Keyboard.OledResource, "apex-oled", WriterPriority.TemporaryUserOverride).Lease
                ?? throw new InvalidOperationException("Another controller owns the OLED.");
            await _link.SendFeatureAsync(report, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (lease is not null) { _arbiter.Release(lease); }
            _io.Release();
        }
    }

    private List<IControlAdapter> BuildAdapters()
    {
        var list = new List<IControlAdapter>();
        foreach (var control in Keyboard.Controls())
        {
            if (!ApexLive.Writes(control.Spec.Setting))
            {
                continue;
            }

            var spec = control.Spec with
            {
                Readable = false,
                PersistsOnDevice = false,
                StoredOnly = false,
                Evidence = "Live ACK; persistence through Save to keyboard. Connecting does not reset the keyboard or erase the OLED.",
            };
            list.Add(new ApexControlAdapter(this, control.Key, spec));
        }

        const string Source = "Vendor HID (0xBC and 0xD2 through the receiver, 0x92 over the cable)";
        list.Add(Battery(DeviceSpecs.Number("battery", "Battery", "Status", new StepRange(0, 100, 1), "%", 0, Source) with { Writable = false, Default = null, PersistsOnDevice = false },
            b => new NumberValue(b.Percent)));
        list.Add(Battery(DeviceSpecs.Toggle("battery.charging", "Charging", "Status", false, Source) with { Writable = false, Default = null, PersistsOnDevice = false },
            b => new ToggleValue(b.Charging)));
        return list;
    }

    private HidAdapter Battery(ControlSpec spec, Func<(int Percent, bool Charging), ControlValue> value) =>
        new(new ControlKey(Keyboard.SettingsResource, spec.Setting), spec, () => _batteryInterface is not null, _arbiter,
            async ct => await ReadBatteryAsync(ct).ConfigureAwait(false) is { } battery ? value(battery) : null, null, null);

    /// <summary>
    /// Receiver connection and battery telemetry share the RGB/profile channel and its transaction gate.
    /// </summary>
    private async Task<(int Percent, bool Charging)?> ReadBatteryAsync(CancellationToken cancellationToken)
    {
        if (TransportDisabledReason is { } reason) throw new InvalidOperationException(reason);
        if (_battery is { } last && Environment.TickCount64 - last.At < 1000)
        {
            return last.Value;
        }

        if (_batteryInterface is null) { throw new InvalidOperationException("Apex Pro TKL Wireless Gen 3 not found."); }
        (int Percent, bool Charging)? value;
        if (_deviceInterface?.Identity.ProductId == 0x1646)
        {
            value = ApexProtocol.Battery((await _link.ReadTelemetryAsync(0x92,cancellationToken).ConfigureAwait(false))[2]);
        }
        else
        {
            var connection = await _link.ReadTelemetryAsync(ApexProtocol.OpcodeConnection, cancellationToken).ConfigureAwait(false);
            value = connection[2] != 1 ? null
                : ApexProtocol.Battery((await _link.ReadTelemetryAsync(0xD2, cancellationToken).ConfigureAwait(false))[2]);
        }
        _battery = (Environment.TickCount64, value);
        return value;
    }

    private sealed class ApexControlAdapter(ApexKeyboardIntegration integration, ControlKey key, ControlSpec spec) : IControlAdapter
    {
        public object BatchGroup => integration;

        public Task WriteBatchAsync(IReadOnlyDictionary<ControlKey, ControlValue> values, Lease lease, CancellationToken cancellationToken) =>
            integration.ApplyControlsAsync(values, lease, cancellationToken);

        public ControlKey Key => key;

        public ControlSpec Spec => spec;

        public bool IsAvailable => integration._deviceInterface is not null;

        public bool WriteEnabled => integration.WriteEnabled;

        public string? WriteDisabledReason => integration.WriteDisabledReason;

        public Task<ControlValue?> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ControlValue?>(null);

        public async Task WriteAsync(ControlValue value, Lease lease, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(lease);
            if (!WriteEnabled)
            {
                throw new InvalidOperationException($"{Spec.Label}: {WriteDisabledReason}");
            }

            if (lease.Resource != Key.Resource || !integration._arbiter.IsCurrent(lease))
            {
                throw new InvalidOperationException("Another writer took control of the device before the write.");
            }

            await integration.ApplyControlsAsync(new Dictionary<ControlKey, ControlValue> { [Key] = value }, lease, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyControlsAsync(IReadOnlyDictionary<ControlKey, ControlValue> values, Lease lease, CancellationToken cancellationToken)
    {
        if (values.Keys.Any(k => k.Resource != lease.Resource || !ApexLive.Writes(k.Setting)))
        {
            throw new InvalidOperationException("The batch contains an unowned control or one without a live command. Nothing was sent.");
        }

        await _io.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!WriteEnabled || !_arbiter.IsCurrent(lease)) { throw new InvalidOperationException(WriteDisabledReason ?? "Keyboard ownership expired before the write."); }
            ApexLive.State next;
            lock (_stateGate) { next = _state.Clone(); }
            var reports = ApexLive.ApplyBatch(next, values.Select(p => new KeyValuePair<string, ControlValue>(p.Key.Setting, p.Value)));
            await _link.SendFeaturesAsync(reports, cancellationToken, () =>
            {
                if (!_arbiter.IsCurrent(lease)) { throw new InvalidOperationException("Another writer took control of the keyboard during the batch."); }
            }).ConfigureAwait(false);
            lock (_stateGate) { _state = next; }
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Sem ACK o mapa na RAM pode ter mudado parcialmente; só outra leitura/ativação o sincroniza.
            _loadedSlot = null;
            throw;
        }
        finally
        {
            _io.Release();
        }
    }
}
