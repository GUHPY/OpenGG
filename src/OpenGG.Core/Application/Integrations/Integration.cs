using System.Globalization;
using OneRGB.Application.Control;

namespace OneRGB.Application.Integrations;

/// <summary>De onde vem o protocolo de uma integração: decide quando a escrita pode ser liberada (ADR-0005).</summary>
public enum IntegrationSource
{
    /// <summary>API documentada do Windows (Core Audio, DDC/CI pelo dxva2, DirectShow, LampArray).</summary>
    SystemApi,

    /// <summary>SDK ou API pública do fabricante (NVML, GameSense).</summary>
    VendorSdk,

    /// <summary>Protocolo publicado pelo fabricante (HID++ 2.0 da Logitech).</summary>
    Documented,

    /// <summary>Engenharia reversa: só escreve depois de conferido no hardware.</summary>
    ReverseEngineered,
}

/// <summary>O que a integração precisa do Windows para escrever.</summary>
public enum IntegrationPrivilege
{
    User,

    /// <summary>Serviço privilegiado do OneRGB (ADR-0004), instalado pelo usuário com UAC.</summary>
    Service,

    /// <summary>Consentimento de privacidade do Windows (câmera, microfone).</summary>
    Consent,
}

public sealed record IntegrationInfo(string Id, string Name, string Family, string Mechanism, IntegrationSource Source, IntegrationPrivilege Privilege = IntegrationPrivilege.User);

/// <summary>O que uma descoberta achou: um adapter por controle, os dispositivos e o limite atual, se houver.</summary>
public sealed record IntegrationDiscovery(IReadOnlyList<IControlAdapter> Adapters, IReadOnlyList<string> Devices, string? Limitation = null)
{
    public static IntegrationDiscovery None(string? limitation = null) => new([], [], limitation);
}

/// <summary>
/// Contrato de toda integração com hardware ou software (plano 1.1). A integração descobre o que existe e devolve
/// adapters; ler e escrever passam só pelo <see cref="ControlService"/> (árbitro, jornal, verificação, desfazer).
/// </summary>
public interface IIntegration
{
    IntegrationInfo Info { get; }

    /// <summary>
    /// Procura os dispositivos e monta os adapters, fora da UI. Devolver a mesma instância de adapter para um controle
    /// que continua presente evita relê-lo. Pode lançar: o host classifica o erro e mantém as outras integrações.
    /// </summary>
    Task<IntegrationDiscovery> DiscoverAsync(CancellationToken cancellationToken);
}

public enum IntegrationHealth
{
    NotStarted,
    Discovering,
    Ready,
    Limited,
    NoDevice,
    Failed,
}

public enum IntegrationAccess
{
    None,
    ReadOnly,
    Partial,
    Full,
}

public sealed record IntegrationStatus(
    IntegrationInfo Info,
    IntegrationHealth Health,
    IReadOnlyList<string> Devices,
    int Controls,
    int Writable,
    IReadOnlyList<string> BlockedBy,
    IReadOnlyList<string> ReadOnlyReasons,
    string? Limitation,
    DeviceError? Error,
    DateTimeOffset? LastRun,
    TimeSpan? Duration)
{
    public IntegrationAccess Access => Controls == 0 ? IntegrationAccess.None
        : Writable == 0 ? IntegrationAccess.ReadOnly
        : Writable < Controls ? IntegrationAccess.Partial
        : IntegrationAccess.Full;

    public static string Text(IntegrationHealth health) => health switch
    {
        IntegrationHealth.NotStarted => "Waiting",
        IntegrationHealth.Discovering => "Procurando",
        IntegrationHealth.Ready => "Pronta",
        IntegrationHealth.Limited => "Limitada",
        IntegrationHealth.NoDevice => "No device",
        _ => "Failed",
    };

    public static string Text(IntegrationAccess access) => access switch
    {
        IntegrationAccess.Full => "controle completo",
        IntegrationAccess.Partial => "controle parcial",
        IntegrationAccess.ReadOnly => "read only",
        _ => "no controls",
    };
}

/// <summary>
/// Roda as integrações (plano 1.1): cada descoberta tem tempo limite e falha sozinha, os adapters entram e saem do
/// <see cref="ControlService"/> por diferença (quem sumiu fica desconectado com o último estado) e o estado de cada
/// uma fica disponível para a tela. Tudo que muda vai para o <see cref="IntegrationLog"/>.
/// </summary>
public sealed class IntegrationHost(ControlService controls, ControlArbiter arbiter, IntegrationLog log, TimeProvider? time = null)
{
    public static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private readonly List<Entry> _entries = [];

    /// <summary>O estado de uma integração mudou (qualquer thread).</summary>
    public event EventHandler<IntegrationStatus>? Changed;

    public IntegrationLog Log => log;

    public void Add(IIntegration integration)
    {
        ArgumentNullException.ThrowIfNull(integration);
        lock (_gate)
        {
            if (_entries.Any(e => e.Integration.Info.Id == integration.Info.Id))
            {
                throw new InvalidOperationException($"Duplicate integration: {integration.Info.Id}.");
            }

            _entries.Add(new Entry(integration));
        }
    }

    public IReadOnlyList<IntegrationStatus> Statuses()
    {
        lock (_gate)
        {
            return [.. _entries.Select(Snapshot)];
        }
    }

    public IntegrationStatus? Status(string id)
    {
        lock (_gate)
        {
            return _entries.FirstOrDefault(e => e.Integration.Info.Id == id) is { } entry ? Snapshot(entry) : null;
        }
    }

    /// <summary>Integração dona de um controle (para o registro de escritas que falharam).</summary>
    public string? OwnerOf(ControlKey key)
    {
        lock (_gate)
        {
            return _entries.FirstOrDefault(e => e.Adapters.ContainsKey(key))?.Integration.Info.Id;
        }
    }

    /// <summary>Chaves que a integração tem no pipeline agora (para o autoteste escolher uma).</summary>
    public IReadOnlyList<ControlKey> KeysOf(string id)
    {
        lock (_gate)
        {
            return _entries.FirstOrDefault(e => e.Integration.Info.Id == id) is { } entry ? [.. entry.Adapters.Keys] : [];
        }
    }

    public Task RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        List<string> ids;
        lock (_gate)
        {
            ids = [.. _entries.Select(e => e.Integration.Info.Id)];
        }

        return Task.WhenAll(ids.Select(id => RefreshAsync(id, cancellationToken)));
    }

    /// <summary>Descobre de novo; duas chamadas seguidas para a mesma integração rodam uma depois da outra.</summary>
    public async Task<IntegrationStatus> RefreshAsync(string id, CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (_gate)
        {
            entry = _entries.FirstOrDefault(e => e.Integration.Info.Id == id) ?? throw new ArgumentException($"Unknown integration: {id}.", nameof(id));
        }

        await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (entry.LastRun is null)
            {
                Publish(entry, () => entry.Health = IntegrationHealth.Discovering);
            }

            var started = _time.GetTimestamp();
            IntegrationDiscovery found;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(DiscoveryTimeout);
                found = await entry.Integration.DiscoverAsync(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Fronteira com o hardware: a falha vira estado da integração, as outras seguem.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                var error = DeviceErrors.Classify(ex is OperationCanceledException ? new TimeoutException($"Discovery exceeded {DiscoveryTimeout.TotalSeconds:0} s.") : ex);
                if (entry.Error?.Message != error.Message)
                {
                    log.Write(id, IntegrationLogLevel.Error, $"Discovery failed ({DeviceErrors.Text(error.Kind)}): {error.Message}");
                }

                // Os adapters atuais ficam: uma descoberta que falhou não prova que o dispositivo saiu.
                return Publish(entry, () =>
                {
                    entry.Health = IntegrationHealth.Failed;
                    entry.Error = error;
                    entry.LastRun = _time.GetUtcNow();
                    entry.Duration = _time.GetElapsedTime(started);
                });
            }

            var fresh = new Dictionary<ControlKey, IControlAdapter>();
            foreach (var adapter in found.Adapters)
            {
                fresh[adapter.Key] = adapter;
            }

            var arrived = new List<ControlKey>();
            foreach (var (key, adapter) in fresh)
            {
                controls.Register(adapter);
                if (!entry.Adapters.TryGetValue(key, out var current) || !ReferenceEquals(current, adapter))
                {
                    arrived.Add(key);
                }
            }

            var gone = entry.Adapters.Keys.Where(k => !fresh.ContainsKey(k)).ToList();
            foreach (var key in gone)
            {
                controls.Disconnect(key);
            }

            lock (_gate)
            {
                entry.Adapters = fresh;
            }

            foreach (var key in arrived)
            {
                await controls.RefreshAsync(key, cancellationToken).ConfigureAwait(false); // falha de leitura vira mensagem no controle
            }

            var devices = found.Devices.Distinct(StringComparer.Ordinal).Order(StringComparer.CurrentCulture).ToList();
            var health = fresh.Count > 0 ? (found.Limitation is null ? IntegrationHealth.Ready : IntegrationHealth.Limited)
                : devices.Count > 0 || found.Limitation is not null ? IntegrationHealth.Limited
                : IntegrationHealth.NoDevice;
            var summary = string.Join(", ", devices);
            if (entry.Error is not null || entry.LastRun is null || summary != entry.DeviceSummary || gone.Count > 0 || found.Limitation != entry.Limitation)
            {
                var text = string.Create(CultureInfo.InvariantCulture, $"{(devices.Count == 0 ? "no devices" : summary)} · {fresh.Count} control(s)");
                log.Write(id, found.Limitation is null ? IntegrationLogLevel.Info : IntegrationLogLevel.Warning,
                    found.Limitation is null ? text : $"{text} · limitation: {found.Limitation}");
            }

            return Publish(entry, () =>
            {
                entry.Health = health;
                entry.Devices = devices;
                entry.DeviceSummary = summary;
                entry.Limitation = found.Limitation;
                entry.Error = null;
                entry.LastRun = _time.GetUtcNow();
                entry.Duration = _time.GetElapsedTime(started);
            });
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    private IntegrationStatus Publish(Entry entry, Action change)
    {
        IntegrationStatus status;
        lock (_gate)
        {
            change();
            status = Snapshot(entry);
        }

        Changed?.Invoke(this, status);
        return status;
    }

    private IntegrationStatus Snapshot(Entry entry)
    {
        var adapters = entry.Adapters.Values;
        var available = adapters.Where(a => a.IsAvailable).ToList();
        var blocked = adapters.Select(a => a.Key.Resource).Distinct().Select(arbiter.ExternalOwnerOf).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var reasons = available.Where(a => !(a.WriteEnabled && a.Spec.Writable))
            .Select(a => a.WriteDisabledReason ?? (a.Spec.Writable ? null : "reported by the device, not adjustable"))
            .OfType<string>().Distinct(StringComparer.Ordinal).Take(3).ToList();
        return new IntegrationStatus(entry.Integration.Info, entry.Health, entry.Devices, available.Count,
            available.Count(a => a.WriteEnabled && a.Spec.Writable), blocked, reasons, entry.Limitation, entry.Error, entry.LastRun, entry.Duration);
    }

    private sealed class Entry(IIntegration integration)
    {
        public IIntegration Integration { get; } = integration;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public Dictionary<ControlKey, IControlAdapter> Adapters { get; set; } = [];

        public IntegrationHealth Health { get; set; }

        public IReadOnlyList<string> Devices { get; set; } = [];

        public string DeviceSummary { get; set; } = string.Empty;

        public string? Limitation { get; set; }

        public DeviceError? Error { get; set; }

        public DateTimeOffset? LastRun { get; set; }

        public TimeSpan? Duration { get; set; }
    }
}

public enum IntegrationLogLevel
{
    Info,
    Warning,
    Error,
}

public sealed record IntegrationLogEntry(DateTimeOffset At, string Integration, IntegrationLogLevel Level, string Message)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{At.ToLocalTime():yyyy-MM-dd HH:mm:ss} [{Level}] {Integration}: {Message}");
}

/// <summary>
/// Registro de integração (plano 1.2): descobertas que mudaram, falhas classificadas, conflitos, permissões e
/// autotestes. Guarda as últimas entradas para a tela e repassa cada uma ao <paramref name="sink"/> (arquivo diário).
/// </summary>
public sealed class IntegrationLog(int capacity = 400, Action<IntegrationLogEntry>? sink = null, TimeProvider? time = null)
{
    private readonly Lock _gate = new();
    private readonly Queue<IntegrationLogEntry> _entries = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Entrada nova (qualquer thread).</summary>
    public event EventHandler<IntegrationLogEntry>? Written;

    public void Write(string integration, IntegrationLogLevel level, string message)
    {
        var entry = new IntegrationLogEntry(_time.GetUtcNow(), integration, level, message);
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > capacity)
            {
                _entries.Dequeue();
            }
        }

        sink?.Invoke(entry);
        Written?.Invoke(this, entry);
    }

    /// <summary>As entradas mais novas primeiro.</summary>
    public IReadOnlyList<IntegrationLogEntry> Recent(int count = 100)
    {
        lock (_gate)
        {
            return [.. _entries.Reverse().Take(count)];
        }
    }
}
