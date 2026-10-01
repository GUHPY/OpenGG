using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using OneRGB.Application.Control;
using OneRGB.Application.Devices;
using OneRGB.Application.Profiles;
using OpenGG.Desktop.Mvvm;

namespace OpenGG.Desktop.ViewModels;

public sealed record ConfigIssue(string Message, bool IsError);

/// <summary>
/// Base das páginas de dispositivo com protocolo de fabricante: a configuração é editada sobre o modelo e validada
/// enquanto se edita. Em tempo real (o padrão), cada mudança vai sozinha, logo depois de parar de mexer, para o perfil
/// do dispositivo (<see cref="DeviceProfiles"/>) e para o dispositivo pelo pipeline; o que o dispositivo recusa volta
/// na tela com o motivo. Páginas de risco (GPU) mantêm o "Aplicar".
/// </summary>
public abstract class DeviceEditorViewModel : ObservableObject
{
    private static readonly TimeSpan CommitDelay = TimeSpan.FromMilliseconds(200);
    private readonly string _pageName;
    private readonly DispatcherTimer _commit;
    private Profile _profile;
    private bool _dirty;
    private string? _status;
    private int _writeQueued;
    private bool _committing;
    private bool _again;
    private bool _statusIsError;
    public bool StatusIsError { get => _statusIsError; protected set => Set(ref _statusIsError, value); }

    // O que a tela mostrava na última carga ou gravação: a próxima gravação leva só o que mudou desde então.
    private Dictionary<ControlKey, ControlValue> _committed = [];

    // O que o dispositivo informava na última carga, para os controles fora do perfil: mudou lá, a tela acompanha.
    private Dictionary<ControlKey, ControlValue?> _seen = [];

    protected DeviceEditorViewModel(AppHost host, MainViewModel main, Dispatcher dispatcher, string deviceId, string deviceName, string pageName)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(dispatcher);
        Host = host;
        Main = main;
        DeviceId = deviceId;
        DeviceName = deviceName;
        _pageName = pageName;
        _profile = Editing();
        _commit = new DispatcherTimer(CommitDelay, DispatcherPriority.Background, async (_, _) => await CommitAsync().ConfigureAwait(true), dispatcher);
        _commit.Stop();

        SaveCommand = new RelayCommand(() => Save(Values()), () => _dirty && !HasErrors);
        ApplyCommand = new RelayCommand(async () => await ApplyAsync().ConfigureAwait(true), () => !HasErrors);
        RevertCommand = new RelayCommand(Load, () => _dirty);
        TakeControlCommand = new RelayCommand(async () => await main.TakeControlAsync(deviceName, [deviceId]).ConfigureAwait(true));
        host.Controls.StatusChanged += (_, status) =>
        {
            if (status.Key.Resource.Value.StartsWith(deviceId + "/", StringComparison.Ordinal) && Interlocked.Exchange(ref _writeQueued, 1) == 0)
            {
                dispatcher.BeginInvoke(DispatcherPriority.Background, OnDeviceChanged);
            }
        };
        host.VendorAppsChanged += (_, _) => RaiseWriteState();

        host.ScanCompleted += (_, _) => dispatcher.BeginInvoke(() =>
        {
            OnPropertyChanged(nameof(Connected));
            OnPropertyChanged(nameof(ConnectionText));
        });
        host.Profiles.Changed += (_, _) => dispatcher.BeginInvoke(ReloadIfSwitched);
        host.SettingsChanged += (_, _) => OnPropertyChanged(nameof(Expert));
    }

    protected AppHost Host { get; }

    protected MainViewModel Main { get; }

    public string DeviceId { get; }

    public string DeviceName { get; }

    public bool Expert => Main.Expert;

    /// <summary>Cada mudança vai sozinha ao dispositivo; falso = a página tem "Aplicar" (ajustes de risco).</summary>
    public virtual bool LiveApply => true;

    public virtual bool Connected => Statuses().Any(Available);

    public virtual string ConnectionText => Connected ? "Connected" : "Not found in the latest scan";

    /// <summary>Apps de fabricante que seguram agora algum recurso deste dispositivo.</summary>
    public IReadOnlyList<string> ExternalOwners => Host.ExternalOwnersOf([DeviceId]);

    public bool HasExternalOwner => ExternalOwners.Count > 0;

    /// <summary>O que o aviso de escrita diz: quem controla, se grava agora, se espera o dispositivo ou o que falta.</summary>
    public virtual string WriteTitle => ExternalOwners is { Count: > 0 } owners ? $"Controlled by {string.Join(" and ", owners)}"
        : WritableGroups().Count > 0 ? (LiveApply ? "Settings apply directly to the device" : "OpenGG writes to the device")
        : Statuses().Any(Available) ? "Read only"
        : "Waiting for the device";

    /// <summary>Detalhe do aviso, sempre com o estado real (nunca o texto fixo da página).</summary>
    public virtual string WriteText
    {
        get
        {
            if (HasExternalOwner)
            {
                return "Close other keyboard controllers before applying settings. Your edits remain in the local profile.";
            }

            var writable = WritableGroups();
            if (writable.Count == 0)
            {
                return Statuses().Any(Available)
                    ? $"Device writes are unavailable: {ReadOnlyReason ?? "no verified write protocol."}"
                    : $"{DeviceName} did not respond. Your edits remain in the local profile.";
            }

            var text = LiveApply
                ? $"Each change applies immediately and is saved to the local profile: {string.Join(", ", writable)}."
                : $"Apply writes directly to the device: {string.Join(", ", writable)}.";
            var locked = LockedGroups();
            return locked.Count == 0 ? text : $"{text} Device writes unavailable: {string.Join(", ", locked)}.";
        }
    }

    public bool CanWriteNow => !HasExternalOwner && WritableGroups().Count > 0;

    public RelayCommand TakeControlCommand { get; }

    /// <summary>Por que algum controle deste dispositivo não grava (motivo técnico, modo avançado); nulo quando tudo grava.</summary>
    public virtual string? ReadOnlyReason => Statuses().Where(s => Available(s) && s.ReadOnlyReason is not null).Select(s => s.ReadOnlyReason).FirstOrDefault();

    public Profile Profile => _profile;

    public string ProfileText => LiveApply
        ? $"Each change applies to the device and is saved to profile \"{_profile.Name}\"."
        : $"Saved to profile \"{_profile.Name}\"{(Host.ProfileLibrary.Find(_profile.Id) is null ? " (created when saved)" : string.Empty)}";

    public ObservableCollection<ConfigIssue> Issues { get; } = [];

    public bool Dirty => _dirty;

    public bool HasErrors => Issues.Any(i => i.IsError);

    public bool HasIssues => Issues.Count > 0;

    /// <summary>Gravando no dispositivo agora.</summary>
    public bool Busy => _committing;

    public string? Status
    {
        get => _status;
        protected set => Set(ref _status, value);
    }

    public RelayCommand SaveCommand { get; }

    public RelayCommand ApplyCommand { get; }

    public RelayCommand RevertCommand { get; }

    /// <summary>Carregando valores na tela: mudanças não contam como edição.</summary>
    protected bool Loading { get; private set; }

    

    /// <summary>Algo na tela mudou: valida de novo e, em tempo real, grava logo depois de parar de mexer.</summary>
    public void Edited()
    {
        if (Loading)
        {
            return;
        }

        _dirty = true;
        StatusIsError = false;
        Status = null;
        Revalidate();
        if (LiveApply)
        {
            _commit.Stop();
            _commit.Start();
        }
    }

    /// <summary>Preenche a tela; <paramref name="valueOf"/> dá o valor do perfil ou, sem ele, o observado.</summary>
    protected abstract void Fill(Func<ControlKey, ControlValue?> valueOf);

    /// <summary>A configuração da tela como valores de controle.</summary>
    protected abstract IReadOnlyDictionary<ControlKey, ControlValue> Values();

    /// <summary>Problemas da configuração da tela, erros primeiro.</summary>
    protected abstract IEnumerable<ConfigIssue> Validate();

    /// <summary>Depois de validar (prévias, códigos derivados).</summary>
    protected virtual void Validated()
    {
    }

    /// <summary>Muda a tela sem marcar como edição (importar, carregar).</summary>
    protected void Quietly(Action change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Loading = true;
        try
        {
            change();
        }
        finally
        {
            Loading = false;
        }
    }

    /// <summary>Lê o perfil em edição de novo (descarta o que não foi gravado) com o que o dispositivo informa agora.</summary>
    protected void Load()
    {
        _commit.Stop();
        _profile = Editing();
        var saved = DeviceProfiles.Values(Host.ProfileLibrary, _profile);
        _seen = Statuses().ToDictionary(s => s.Key, s => s.Observed);
        Quietly(() => Fill(key => saved.GetValueOrDefault(key) ?? Host.Controls.Status(key)?.Observed));
        _committed = new Dictionary<ControlKey, ControlValue>(Values());
        _dirty = false;
        Status = null;
        Revalidate();
        OnPropertyChanged(nameof(ProfileText));
    }

    /// <summary>Adota uma leitura do dispositivo como base da próxima edição, sem enviar valores inalterados.</summary>
    protected void ImportValues(Func<ControlKey, ControlValue?> valueOf)
    {
        _commit.Stop();
        Quietly(() => Fill(valueOf));
        _committed = new Dictionary<ControlKey, ControlValue>(Values());
        _dirty = false;
        Revalidate();
    }

    protected void Revalidate()
    {
        Issues.Clear();
        foreach (var issue in Validate())
        {
            Issues.Add(issue);
        }

        Validated();
        OnPropertyChanged(nameof(Dirty));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasIssues));
        CommandManager.InvalidateRequerySuggested();
    }

    private static bool Available(ControlStatus status) => status.State is not (FeatureState.Unavailable or FeatureState.Disconnected);

    private IReadOnlyList<ControlStatus> Statuses() =>
        [.. Host.Controls.Statuses().Where(s => s.Key.Resource.Value.StartsWith(DeviceId + "/", StringComparison.Ordinal))];

    /// <summary>Grupos (HITS, Imagem, Base…) com algum controle que o OpenGG grava agora neste dispositivo.</summary>
    private List<string> WritableGroups() =>
    [
        .. Statuses().Where(s => s.Spec.Writable && !s.Spec.StoredOnly && s.ReadOnlyReason is null && Available(s))
            .Select(GroupOf)
            .Distinct(StringComparer.Ordinal),
    ];

    /// <summary>Grupos em que nenhum controle grava (sem protocolo de escrita ainda).</summary>
    private List<string> LockedGroups()
    {
        var writable = WritableGroups();
        return [.. Statuses().Where(s => s.Spec.Writable && Available(s) && (s.Spec.StoredOnly || s.ReadOnlyReason is not null)).Select(GroupOf)
            .Distinct(StringComparer.Ordinal).Where(g => !writable.Contains(g, StringComparer.Ordinal))];
    }

    private static string GroupOf(ControlStatus status) => status.Spec.Group.Length > 0 ? status.Spec.Group : status.Spec.Label;

    private void OnDeviceChanged()
    {
        RaiseWriteState();

        // O dispositivo mudou um valor que o perfil não fixa (outro app, o próprio dispositivo, a primeira leitura):
        // a tela acompanha, desde que ninguém esteja editando.
        if (_dirty || _committing || Loading)
        {
            return;
        }

        // Só o que a tela edita: bateria e ChatMix mudam sozinhos e não refazem a página.
        var saved = DeviceProfiles.Values(Host.ProfileLibrary, _profile);
        var shown = Values();
        if (Statuses().Any(s => s.Spec.Writable && shown.ContainsKey(s.Key) && !saved.ContainsKey(s.Key) && s.Observed is { } observed
            && !(_seen.GetValueOrDefault(s.Key) is { } seen && seen.Matches(observed, s.Spec.Tolerance))))
        {
            Load();
        }
    }

    private void RaiseWriteState()
    {
        Interlocked.Exchange(ref _writeQueued, 0);
        OnPropertyChanged(nameof(ExternalOwners));
        OnPropertyChanged(nameof(HasExternalOwner));
        OnPropertyChanged(nameof(WriteTitle));
        OnPropertyChanged(nameof(WriteText));
        OnPropertyChanged(nameof(CanWriteNow));
        OnPropertyChanged(nameof(ReadOnlyReason));
        OnPropertyChanged(nameof(Connected));
        OnPropertyChanged(nameof(ConnectionText));
    }

    private Profile Editing() => DeviceProfiles.Editing(Host.ProfileLibrary, Host.Profiles.Active.Select(a => a.Profile.Id), DeviceId, DeviceName);

    /// <summary>Outro perfil do dispositivo passou a valer (regra ou usuário): mostra o dele se nada foi editado.</summary>
    private void ReloadIfSwitched()
    {
        var next = Editing();
        if (!_dirty && !_committing && (!string.Equals(next.Id, _profile.Id, StringComparison.Ordinal) || next.Modified != _profile.Modified))
        {
            Load();
        }
    }

    /// <summary>Guarda estes valores no perfil (os outros ajustes dele ficam). Devolve falso com o motivo em <see cref="Status"/>.</summary>
    private bool Save(IReadOnlyDictionary<ControlKey, ControlValue> values)
    {
        var updated = DeviceProfiles.With(_profile, values, DateTimeOffset.Now);
        if (Host.ProfileLibrary.Save(updated).FirstOrDefault(i => i.Blocking) is { } issue)
        {
            Status = $"Not saved: {issue.Message}";
            return false;
        }

        _profile = updated;
        foreach (var (key, value) in values)
        {
            _committed[key] = value;
        }

        _dirty = !Values().All(v => _committed.TryGetValue(v.Key, out var old) && old.Equals(v.Value));
        if (!LiveApply)
        {
            Status = $"Saved to profile \"{_profile.Name}\".";
        }

        Revalidate();
        OnPropertyChanged(nameof(ProfileText));
        return true;
    }

    /// <summary>Uma gravação por vez; mudanças durante ela viram mais uma rodada com os valores mais novos.</summary>
    private async Task CommitAsync()
    {
        _commit.Stop();
        if (_committing)
        {
            _again = true;
            return;
        }

        _committing = true;
        OnPropertyChanged(nameof(Busy));
        try
        {
            do
            {
                _again = false;
                await CommitOnceAsync().ConfigureAwait(true);
            }
            while (_again);
        }
        finally
        {
            _committing = false;
            OnPropertyChanged(nameof(Busy));
        }
    }

    /// <summary>
    /// Guarda no perfil só o que mudou na tela e leva isso ao dispositivo agora. O que o dispositivo recusa sai do perfil
    /// e volta na tela; o que não pôde ir porque ele não está conectado fica no perfil e vai quando conectar.
    /// </summary>
    private async Task CommitOnceAsync()
    {
        if (HasErrors)
        {
            StatusIsError = true;
            Status = "Fix the errors highlighted in red; nothing was written.";
            return;
        }

        var changed = Values().Where(v => !_committed.TryGetValue(v.Key, out var old) || !old.Equals(v.Value)).ToDictionary(v => v.Key, v => v.Value);
        if (changed.Count == 0)
        {
            _dirty = false;
            OnPropertyChanged(nameof(Dirty));
            return;
        }

        var before = _profile;
        if (!Save(changed))
        {
            return;
        }

        // A página fala pelo usuário: um ajuste manual antigo pela lista de controles não segura o dispositivo contra ela.
        foreach (var resource in changed.Keys.Select(k => k.Resource).Distinct())
        {
            Host.Controls.Release(resource, "user");
        }

        var result = await Host.Profiles.ApplyEditAsync(_profile.Id, [.. changed.Keys], $"Edit on the {_pageName} page").ConfigureAwait(true);
        var mine = result.Settings.Where(s => changed.ContainsKey(s.Key)).ToList();
        var refused = mine.Where(s => s.Outcome is CommandOutcome.Failed or CommandOutcome.Rejected or CommandOutcome.RolledBack or CommandOutcome.Cancelled).ToList();
        if (refused.Count > 0)
        {
            StatusIsError = true;
            foreach (var c in changed)
            {
                _committed[c.Key] = c.Value;
            }

            Status = $"Saved locally (the device did not receive it: {string.Join(" ", refused.Select(r => r.Message))})";
            return;
        }

        StatusIsError = mine.Any(s => s.Outcome == CommandOutcome.Blocked);
        Status = Describe(mine, result, key => Host.Controls.Status(key)?.Spec.StoredOnly == true);
    }

    private static string Describe(List<SettingResult> mine, ProfileApplyResult result, Func<ControlKey, bool> storedOnly)
    {
        if (mine.Count == 0)
        {
            return result.Message;
        }

        if (mine.TrueForAll(s => s.Skipped))
        {
            return $"Saved locally; {mine[0].Message}";
        }

        if (mine.Find(s => s.Outcome is CommandOutcome.Blocked) is { } blocked)
        {
            return $"Saved locally; the device did not receive it: {blocked.Message}";
        }

        if (mine.Count <= 2)
        {
            return string.Join(" ", mine.Select(s => s.Message));
        }

        // Sem integração própria o valor não sai do OpenGG: nunca contar como gravado no dispositivo.
        var stored = mine.Count(s => storedOnly(s.Key));
        return stored == 0 ? $"{mine.Count} settings applied to the device."
            : stored == mine.Count ? $"{stored} settings saved locally in OpenGG; device writes are unavailable."
            : $"{mine.Count - stored} settings applied to the device; {stored} saved locally in OpenGG.";
    }

    /// <summary>Salva e ativa o perfil (páginas com "Aplicar"): o pipeline leva ao dispositivo o que puder e diz o que ficou bloqueado.</summary>
    private async Task ApplyAsync()
    {
        if ((_dirty || Host.ProfileLibrary.Find(_profile.Id) is null) && !Save(Values()))
        {
            return;
        }

        var result = await Host.Profiles.ActivateAsync(_profile.Id, ActivationCause.ByUser($"Apply from the {_pageName} page")).ConfigureAwait(true);
        var blocked = result.Settings.FirstOrDefault(s => s.Outcome == CommandOutcome.Blocked || s.Skipped);
        Status = result.AppliedCount > 0 || blocked is null
            ? result.Message
            : $"Saved locally; the device did not receive it: {blocked.Message}";
    }
}
