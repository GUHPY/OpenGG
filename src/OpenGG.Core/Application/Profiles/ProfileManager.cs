using System.Globalization;
using OneRGB.Application.Control;

namespace OneRGB.Application.Profiles;

public enum ProfileOutcome
{
    Applied,
    Partial,
    Failed,
    Suppressed,
    Rejected,
    Superseded,
}

/// <summary>Resultado de um ajuste do perfil. Sem <see cref="Outcome"/> = pulado, com o motivo.</summary>
public sealed record SettingResult(ControlKey Key, string Label, ControlValue Target, CommandOutcome? Outcome, string Message)
{
    public bool Skipped => Outcome is null;
}

public sealed record ProfileApplyResult(string ProfileId, ProfileOutcome Outcome, string Message, IReadOnlyList<SettingResult> Settings)
{
    public int AppliedCount => Settings.Count(s => s.Outcome is CommandOutcome.Verified or CommandOutcome.Applied);

    public int AwaitingCount => Settings.Count(s => s.Outcome is CommandOutcome.AwaitingConfirmation);

    public int FailedCount => Settings.Count(s => s.Outcome is CommandOutcome.Failed or CommandOutcome.RolledBack or CommandOutcome.Rejected or CommandOutcome.Cancelled);

    public int SkippedCount => Settings.Count(s => s.Skipped || s.Outcome is CommandOutcome.Blocked);
}

/// <summary>Registro de ativação (spec §381): qual perfil, por quê, com qual prioridade e o que aconteceu.</summary>
public sealed record ProfileActivationRecord(DateTimeOffset Time, string ProfileId, string ProfileName, ProfileLayer Layer, bool Activated, ActivationCause Cause, ProfileApplyResult Result)
{
    public string Explanation
    {
        get
        {
            var lines = new List<string>
            {
                $"{(Activated ? "Profile activated" : "Profile deactivated")}: {ProfileName}",
                $"Motivo: {(Cause.RuleName is { } rule ? $"regra \"{rule}\" — " : string.Empty)}{Cause.Reason}",
            };
            if (Cause.MatchedExecutable is { } exe)
            {
                lines.Add($"Executable: {exe}");
            }

            lines.Add($"Priority: {ProfileResolver.LayerText(Layer)} profile");
            lines.Add($"Resultado: {Result.Message}");
            return string.Join(Environment.NewLine, lines);
        }
    }
}

/// <summary>
/// Ativa e desativa perfis (spec §22, §23, A31, A5.12). Mantém um global, um por dispositivo e um de aplicativo,
/// resolve o valor final de cada controle, aplica pela <see cref="ControlService"/> na ordem de segurança e registra
/// o porquê de cada ativação. Ajustes manuais ficam por cima (prioridade do usuário) até serem salvos ou revertidos;
/// ao sair um perfil, cada controle volta ao valor da camada de baixo ou ao que era antes dos perfis.
/// </summary>
public sealed class ProfileManager : IDisposable
{
    /// <summary>Escritor com que os perfis aparecem no árbitro e no jornal.</summary>
    public const string Writer = "Perfis";

    private const int HistoryCapacity = 100;
    private readonly ControlService _controls;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _apply = new(1, 1);
    private readonly List<ProfileActivation> _active = [];
    private readonly Dictionary<string, StateMachine<ProfileState>> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<ControlKey, ControlValue> _baseline = [];
    private readonly Dictionary<ResourceId, WriterPriority> _held = [];
    private readonly Dictionary<ControlKey, string> _overrides = [];
    private readonly LinkedList<ProfileActivationRecord> _history = new();
    private IReadOnlyList<ResolvedSetting> _resolved = [];
    private long _order;
    private long _version;

    public ProfileManager(ControlService controls, ProfileLibrary library, TimeProvider? time = null)
    {
        _controls = controls ?? throw new ArgumentNullException(nameof(controls));
        Library = library ?? throw new ArgumentNullException(nameof(library));
        _time = time ?? TimeProvider.System;
        _controls.Journal.Changed += OnJournalChanged;
    }

    /// <summary>Um perfil foi ativado ou desativado (com a explicação).</summary>
    public event EventHandler<ProfileActivationRecord>? Activated;

    /// <summary>Estado geral mudou (perfis ativos, estados, ajustes manuais).</summary>
    public event EventHandler? Changed;

    public ProfileLibrary Library { get; }

    public IReadOnlyList<ProfileActivation> Active
    {
        get
        {
            lock (_gate)
            {
                return [.. _active];
            }
        }
    }

    public IReadOnlyList<ResolvedSetting> Resolved
    {
        get
        {
            lock (_gate)
            {
                return _resolved;
            }
        }
    }

    public IReadOnlyList<ProfileActivationRecord> History
    {
        get
        {
            lock (_gate)
            {
                return [.. _history];
            }
        }
    }

    /// <summary>Perfil de aplicativo escolhido à mão: vence a troca automática até o usuário liberar (A5.12).</summary>
    public string? HeldApplicationProfile { get; private set; }

    /// <summary>Controles mudados por fora do perfil (ajuste manual, automação), com o escritor.</summary>
    public IReadOnlyDictionary<ControlKey, string> Overrides
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<ControlKey, string>(_overrides);
            }
        }
    }

    public ProfileState StateOf(string profileId)
    {
        lock (_gate)
        {
            return _states.TryGetValue(profileId, out var machine) ? machine.State : ProfileState.Inactive;
        }
    }

    public ResolvedSetting? ResolvedFor(ControlKey key) => Resolved.FirstOrDefault(r => r.Key == key);

    /// <summary>Ativa um perfil no lugar do que ocupa a mesma vaga (global, o dispositivo dele, ou aplicativo).</summary>
    public async Task<ProfileApplyResult> ActivateAsync(string profileId, ActivationCause cause, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cause);
        var version = Interlocked.Increment(ref _version);
        await _apply.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profile = Library.Find(profileId);
            if (profile is null)
            {
                return Record(profileId, profileId, ProfileLayer.Global, true, cause, new ProfileApplyResult(profileId, ProfileOutcome.Rejected, "Profile not found.", []));
            }

            var chain = Library.Ancestry(profile.Id).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            if (Library.Validate().FirstOrDefault(i => i.Blocking && chain.Contains(i.ProfileId)) is { } issue)
            {
                return Record(profile, true, cause, new ProfileApplyResult(profile.Id, ProfileOutcome.Rejected, $"Invalid profile: {issue.Message}", []));
            }

            if (profile.Layer == ProfileLayer.Application && cause.Kind == ActivationKind.Automatic
                && HeldApplicationProfile is { } held && !string.Equals(held, profile.Id, StringComparison.Ordinal))
            {
                var heldName = Library.Find(held)?.Name ?? held;
                return Record(profile, true, cause, new ProfileApplyResult(profile.Id, ProfileOutcome.Suppressed,
                    $"Skipped: profile \"{heldName}\" was selected manually and takes priority over automatic switching.", []));
            }

            List<ProfileActivation> next;
            ProfileActivation? replaced;
            lock (_gate)
            {
                next = [.. _active];
                replaced = next.FirstOrDefault(a => SameSlot(a.Profile, profile));
                if (replaced is not null)
                {
                    next.Remove(replaced);
                }

                next.Add(new ProfileActivation(profile, cause, ++_order, _time.GetUtcNow()));
                if (profile.Layer == ProfileLayer.Application && cause.Kind == ActivationKind.Manual)
                {
                    HeldApplicationProfile = profile.Id;
                }
            }

            if (replaced is not null && !string.Equals(replaced.Profile.Id, profile.Id, StringComparison.Ordinal))
            {
                Move(replaced.Profile.Id, ProfileState.Superseded, $"Replaced by \"{profile.Name}\"");
            }

            if (StateOf(profile.Id) is ProfileState.Inactive or ProfileState.Superseded or ProfileState.Error)
            {
                Move(profile.Id, ProfileState.Pending, cause.Reason);
            }

            Move(profile.Id, ProfileState.Applying, cause.Reason);

            // Escolha explícita do usuário vence ajustes manuais antigos nos controles deste perfil (A5.12).
            if (cause.Kind == ActivationKind.Manual)
            {
                ReleaseOverrides(Library.Effective(profile.Id).Select(s => s.Key));
            }

            var result = await ApplyAsync(next, profile, cause, version, null, cancellationToken).ConfigureAwait(false);
            Move(profile.Id, result.Outcome == ProfileOutcome.Failed ? ProfileState.Error : ProfileState.Active, result.Message);
            return Record(profile, true, cause, result);
        }
        finally
        {
            _apply.Release();
        }
    }

    /// <summary>Tira um perfil; os controles voltam para a camada de baixo ou ao valor de antes dos perfis.</summary>
    public async Task<ProfileApplyResult> DeactivateAsync(string profileId, ActivationCause cause, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cause);
        var version = Interlocked.Increment(ref _version);
        await _apply.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<ProfileActivation> next;
            ProfileActivation? removed;
            lock (_gate)
            {
                removed = _active.FirstOrDefault(a => string.Equals(a.Profile.Id, profileId, StringComparison.Ordinal));
                next = [.. _active.Where(a => !ReferenceEquals(a, removed))];
                if (removed is not null && cause.Kind == ActivationKind.Manual && string.Equals(HeldApplicationProfile, profileId, StringComparison.Ordinal))
                {
                    HeldApplicationProfile = null;
                }
            }

            if (removed is null)
            {
                return new ProfileApplyResult(profileId, ProfileOutcome.Rejected, "The profile is inactive.", []);
            }

            if (removed.Profile.Layer == ProfileLayer.Application && cause.Kind == ActivationKind.Automatic
                && string.Equals(HeldApplicationProfile, profileId, StringComparison.Ordinal))
            {
                // Escolhido à mão: a regra que o ativaria/desativaria não manda nele.
                return Record(removed.Profile, false, cause, new ProfileApplyResult(profileId, ProfileOutcome.Suppressed, "Kept: manually selected profile.", []));
            }

            var result = await ApplyAsync(next, removed.Profile, cause, version, null, cancellationToken).ConfigureAwait(false);
            Move(profileId, ProfileState.Inactive, cause.Reason);
            return Record(removed.Profile, false, cause, result);
        }
        finally
        {
            _apply.Release();
        }
    }

    /// <summary>Reaplica o estado dos perfis (dispositivo reconectou, retorno da suspensão): A5.12.</summary>
    public async Task<ProfileApplyResult> ReapplyAsync(ResourceId? resource = null, CancellationToken cancellationToken = default)
    {
        var version = Interlocked.Increment(ref _version);
        await _apply.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<ProfileActivation> current;
            lock (_gate)
            {
                current = [.. _active];
            }

            var top = current.OrderByDescending(a => a.Profile.Layer).ThenByDescending(a => a.Order).FirstOrDefault();
            if (top is null)
            {
                return new ProfileApplyResult(string.Empty, ProfileOutcome.Rejected, "No active profile.", []);
            }

            return await ApplyAsync(current, top.Profile, new ActivationCause(ActivationKind.Restore, "Reaplicado"), version, resource, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _apply.Release();
        }
    }

    /// <summary>
    /// A página do dispositivo mudou o perfil dele (tempo real, sem "Aplicar"): só os controles mudados vão ao
    /// dispositivo, por cima de ajustes manuais antigos neles, sem entrar no histórico de ativações a cada ajuste.
    /// Perfil que ainda não vale é ativado (aí vai tudo dele).
    /// </summary>
    public async Task<ProfileApplyResult> ApplyEditAsync(string profileId, IReadOnlyCollection<ControlKey> keys, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var cause = ActivationCause.ByUser(reason);
        if (!Active.Any(a => string.Equals(a.Profile.Id, profileId, StringComparison.Ordinal)))
        {
            return await ActivateAsync(profileId, cause, cancellationToken).ConfigureAwait(false);
        }

        var version = Interlocked.Increment(ref _version);
        await _apply.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<ProfileActivation> current;
            lock (_gate)
            {
                current = [.. _active];
            }

            if (current.FirstOrDefault(a => string.Equals(a.Profile.Id, profileId, StringComparison.Ordinal)) is not { } subject)
            {
                return new ProfileApplyResult(profileId, ProfileOutcome.Rejected, "The profile became inactive before applying.", []);
            }

            ReleaseOverrides(keys);
            return await ApplyAsync(current, subject.Profile, cause, version, null, cancellationToken, keys.ToHashSet()).ConfigureAwait(false);
        }
        finally
        {
            _apply.Release();
        }
    }

    /// <summary>Descarta os ajustes manuais e volta aos valores dos perfis ("Voltar ao perfil").</summary>
    public async Task<ProfileApplyResult> RevertOverridesAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<ControlKey, string> overrides;
        lock (_gate)
        {
            overrides = new Dictionary<ControlKey, string>(_overrides);
        }

        ReleaseOverrides(overrides.Keys);
        var result = await ReapplyAsync(null, cancellationToken).ConfigureAwait(false);
        foreach (var activation in Active.Where(a => StateOf(a.Profile.Id) == ProfileState.Overridden))
        {
            Move(activation.Profile.Id, ProfileState.Active, "Manual overrides discarded");
        }

        return result;
    }

    /// <summary>
    /// Devolve um controle ao perfil depois de um ajuste por fora (regra de automação saindo, "voltar ao perfil" de um
    /// controle): solta a posse do escritor, esquece o ajuste e reaplica o valor do perfil que manda nele. Falso se
    /// nenhum perfil cobre o controle — quem chamou volta o valor antigo por conta própria.
    /// </summary>
    public async Task<bool> RestoreAsync(ControlKey key, string writer, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(writer);
        string? owner;
        lock (_gate)
        {
            owner = _resolved.FirstOrDefault(r => r.Key == key)?.ProfileId;
            if (_overrides.TryGetValue(key, out var by) && string.Equals(by, writer, StringComparison.Ordinal))
            {
                _overrides.Remove(key);
            }
        }

        _controls.Release(key.Resource, writer);
        if (owner is null)
        {
            return false;
        }

        await ReapplyAsync(key.Resource, cancellationToken).ConfigureAwait(false);
        bool stillOverridden;
        lock (_gate)
        {
            stillOverridden = _overrides.Keys.Any(k => _resolved.Any(r => r.Key == k && string.Equals(r.ProfileId, owner, StringComparison.Ordinal)));
        }

        if (!stillOverridden && StateOf(owner) == ProfileState.Overridden)
        {
            Move(owner, ProfileState.Active, "Setting restored to profile");
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    /// <summary>Guarda os ajustes manuais atuais no perfil que manda em cada controle ("Salvar no perfil").</summary>
    public IReadOnlyList<ProfileIssue> SaveOverrides()
    {
        var issues = new List<ProfileIssue>();
        List<ResolvedSetting> overridden;
        lock (_gate)
        {
            overridden = [.. _resolved.Where(r => _overrides.ContainsKey(r.Key))];
        }

        List<(ResolvedSetting Resolved, ControlValue Value)> changes = [];
        foreach (var resolved in overridden)
        {
            if (_controls.Status(resolved.Key)?.Applied is { } applied)
            {
                changes.Add((resolved, applied));
            }
        }

        foreach (var group in changes.GroupBy(c => c.Resolved.ProfileId))
        {
            if (Library.Find(group.Key) is not { } profile)
            {
                continue;
            }

            var settings = profile.Settings.ToList();
            foreach (var (resolved, value) in group)
            {
                var index = settings.FindIndex(s => s.Key == resolved.Key);
                var updated = new ProfileSetting { Key = resolved.Key, Value = value };
                if (index >= 0)
                {
                    settings[index] = updated;
                }
                else
                {
                    settings.Add(updated);
                }
            }

            issues.AddRange(Library.Save(profile with { Settings = settings, Modified = _time.GetUtcNow() }));
        }

        ReleaseOverrides([.. changes.Select(c => c.Resolved.Key)]);
        foreach (var activation in Active.Where(a => StateOf(a.Profile.Id) == ProfileState.Overridden))
        {
            bool remaining;
            lock (_gate)
            {
                remaining = _overrides.Keys.Any(k => _resolved.Any(r => r.Key == k && string.Equals(r.ProfileId, activation.Profile.Id, StringComparison.Ordinal)));
            }

            if (!remaining)
            {
                Move(activation.Profile.Id, ProfileState.Active, "Manual changes saved to profile");
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return issues;
    }

    /// <summary>Libera a escolha manual do perfil de aplicativo: a automação volta a trocar.</summary>
    public void ReleaseHold()
    {
        HeldApplicationProfile = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>O que ativar o perfil mudaria agora (prévia, ADR-0005 "draft/preview").</summary>
    public IReadOnlyList<PlannedChange> Preview(string profileId)
    {
        var profile = Library.Find(profileId);
        if (profile is null)
        {
            return [];
        }

        List<ProfileActivation> next;
        lock (_gate)
        {
            next = [.. _active.Where(a => !SameSlot(a.Profile, profile)), new ProfileActivation(profile, ActivationCause.ByUser(), _order + 1, _time.GetUtcNow())];
        }

        var resolved = ProfileResolver.Resolve(Library, next);
        return _controls.Preview(resolved.Select(r => new ControlRequest(r.Key, r.Value, Writer, r.Priority)));
    }

    /// <summary>
    /// Cria um perfil com os valores atuais dos controles ("Salvar estado atual como perfil"). Lê do dispositivo o
    /// que ainda não foi lido; controles só de leitura ou sem valor conhecido ficam de fora.
    /// </summary>
    public async Task<Profile> CaptureAsync(string id, string name, ProfileLayer layer, IEnumerable<ControlKey> keys, string? deviceId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var settings = new List<ProfileSetting>();
        foreach (var key in keys.Distinct())
        {
            if (_controls.Status(key) is not { Spec.Writable: true } status || status.State is FeatureState.Unavailable or FeatureState.Disconnected)
            {
                continue;
            }

            // Leitura que falha devolve nulo: o controle só fica fora do perfil capturado.
            var value = status.Observed ?? status.Applied ?? await _controls.RefreshAsync(key, cancellationToken).ConfigureAwait(false);

            if (value is not null)
            {
                settings.Add(new ProfileSetting { Key = key, Value = value });
            }
        }

        return new Profile { Id = id, Name = name, Layer = layer, DeviceId = deviceId, Settings = settings, Modified = _time.GetUtcNow() };
    }

    public void Dispose()
    {
        _controls.Journal.Changed -= OnJournalChanged;
        _apply.Dispose();
    }

    private async Task<ProfileApplyResult> ApplyAsync(List<ProfileActivation> next, Profile subject, ActivationCause cause, long version, ResourceId? only, CancellationToken cancellationToken,
        HashSet<ControlKey>? keys = null)
    {
        var resolved = ProfileResolver.Resolve(Library, next);
        IReadOnlyList<ResolvedSetting> previous;
        lock (_gate)
        {
            previous = _resolved;
            _active.Clear();
            _active.AddRange(next);
            _resolved = resolved;
        }

        var covered = resolved.Select(r => r.Key).ToHashSet();
        lock (_gate)
        {
            // Ajuste manual só é "por cima de um perfil" enquanto algum perfil cobre o controle.
            foreach (var key in _overrides.Keys.Where(k => !covered.Contains(k)).ToList())
            {
                _overrides.Remove(key);
            }
        }

        foreach (var setting in resolved)
        {
            _controls.SetPersistent(setting.Key, setting.Value);
        }

        // Controles que saíram de todos os perfis: voltam ao valor de antes dos perfis, quando conhecido.
        var restores = new List<(ControlKey Key, ControlValue Value)>();
        foreach (var gone in previous.Where(p => !covered.Contains(p.Key)))
        {
            _controls.SetPersistent(gone.Key, null);
            lock (_gate)
            {
                if (_baseline.TryGetValue(gone.Key, out var original))
                {
                    restores.Add((gone.Key, original));
                }
            }
        }

        // Prioridade por recurso: a mais alta entre os ajustes que o perfil aplica nele (o árbitro é por recurso).
        var priority = resolved.GroupBy(r => r.Key.Resource).ToDictionary(g => g.Key, g => g.Max(r => r.Priority));
        // Trocar o perfil da memória do mouse (B10) muda o que o resto do recurso lê e grava: a edição dele leva o resto junto.
        var switching = keys?.Where(k => _controls.Status(k)?.Spec.SwitchesResource == true).Select(k => k.Resource).ToHashSet();
        var plan = resolved.Select(r => (r.Key, r.Value, Resolved: (ResolvedSetting?)r))
            .Concat(restores.Select(r => (r.Key, r.Value, Resolved: (ResolvedSetting?)null)))
            .Where(p => (only is null || p.Key.Resource == only) && (keys is null || keys.Contains(p.Key) || switching?.Contains(p.Key.Resource) == true))
            .ToList();
        var ordered = ApplyOrder.Sort(plan, p => p.Key, k => _controls.Status(k)?.Spec);

        var results = new List<SettingResult>();
        var unprotected = new HashSet<string>(StringComparer.Ordinal);
        var failedResources = new HashSet<ResourceId>();
        var provider = CultureInfo.CurrentCulture;
        var stopped = false;
        var switched = new HashSet<ResourceId>();
        var batchResults = new Dictionary<ControlKey, CommandResult>();
        for (var ordinal = 0; ordinal < ordered.Count; ordinal++)
        {
            var (key, value, source) = ordered[ordinal];
            var status = _controls.Status(key);
            var label = status?.Spec.Label ?? key.Setting;
            if (stopped || Interlocked.Read(ref _version) != version)
            {
                stopped = true;
                results.Add(new SettingResult(key, label, value, CommandOutcome.Superseded, "Superseded by a newer profile switch."));
                continue;
            }

            if (status is null || status.State is FeatureState.Unavailable or FeatureState.Disconnected)
            {
                results.Add(new SettingResult(key, label, value, null, "Device unavailable: applies when connected."));
                continue;
            }

            var spec = status.Spec;
            if (spec.Risk == RiskLevel.High && cause.Kind == ActivationKind.Automatic && !subject.AllowHighRiskAutomatic)
            {
                results.Add(new SettingResult(key, label, value, null, "High risk: requires manual activation or explicit profile permission (spec §382)."));
                continue;
            }

            var device = key.Resource.Value.Split('/')[0];
            if (spec.Phase == ApplyPhase.Performance && unprotected.Contains(device))
            {
                results.Add(new SettingResult(key, label, value, null, "Skipped: this device's fan/limit safeguards have not been verified."));
                continue;
            }

            if (failedResources.Contains(key.Resource))
            {
                results.Add(new SettingResult(key, label, value, null, "Skipped: an earlier setting for this resource failed. Applying stopped."));
                continue;
            }

            bool overridden;
            lock (_gate)
            {
                overridden = _overrides.ContainsKey(key);
            }

            if (!batchResults.ContainsKey(key) && source is not null && !switched.Contains(key.Resource) && status.Owner is Writer && status.Applied is { } applied && applied.Matches(value, spec.Tolerance)
                && (status.Observed is null || status.Observed.Matches(value, spec.Tolerance)))
            {
                results.Add(new SettingResult(key, label, value, CommandOutcome.Verified, "No change."));
                continue;
            }

            var target = source?.Priority ?? (priority.TryGetValue(key.Resource, out var held) ? held : WriterPriority.GlobalProfile);
            var wanted = priority.TryGetValue(key.Resource, out var top) ? top : target;
            EnsurePriority(key.Resource, wanted);
            var reason = source is null
                ? "Restoring the value from before profile activation"
                : $"{ProfileResolver.LayerText(source.Layer)} profile \"{source.ProfileName}\" — {cause.Reason}";
            if (!batchResults.ContainsKey(key) && _controls.BatchGroup(key) is { } group)
            {
                var batch = ordered.Skip(ordinal).TakeWhile(p => p.Key.Resource == key.Resource
                    && _controls.Status(p.Key)?.Spec.Phase == spec.Phase && ReferenceEquals(_controls.BatchGroup(p.Key), group))
                    .Select(p => new ControlRequest(p.Key, p.Value, Writer, wanted, reason)).ToList();
                if (batch.Count > 1)
                {
                    foreach (var item in await _controls.ExecuteBatchAsync(batch, cancellationToken).ConfigureAwait(false)) { batchResults[item.Key] = item; }
                }
            }
            var result = batchResults.GetValueOrDefault(key)
                ?? await _controls.ExecuteAsync(new ControlRequest(key, value, Writer, wanted, reason), cancellationToken).ConfigureAwait(false);
            if (result.Outcome is CommandOutcome.Failed or CommandOutcome.RolledBack or CommandOutcome.Cancelled)
            {
                failedResources.Add(key.Resource);
            }
            if (result.Succeeded && result.Before is { } before)
            {
                lock (_gate)
                {
                    if (source is null)
                    {
                        _baseline.Remove(key);
                    }
                    else
                    {
                        _baseline.TryAdd(key, before);
                    }
                }
            }

            if (!result.Succeeded && spec.Phase == ApplyPhase.Protective)
            {
                unprotected.Add(device);
            }

            if (result.Succeeded && spec.SwitchesResource && (result.Before is not { } was || !was.Matches(value, spec.Tolerance)))
            {
                switched.Add(key.Resource); // o resto do recurso agora lê outro lugar: o "unchanged" de antes não vale
            }

            var message = result.Outcome == CommandOutcome.Blocked && overridden
                ? "Manual setting kept (user priority)."
                : result.Message;
            results.Add(new SettingResult(key, label, value, result.Outcome, message));
        }

        // Recursos que nenhum perfil cobre mais: a posse volta a ficar livre.
        List<ResourceId> released;
        lock (_gate)
        {
            released = [.. _held.Keys.Where(r => !priority.ContainsKey(r) && (only is null || r == only))];
        }

        foreach (var resource in released)
        {
            _controls.Release(resource, Writer);
            lock (_gate)
            {
                _held.Remove(resource);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return Summarize(subject.Id, results, stopped, provider);
    }

    private void EnsurePriority(ResourceId resource, WriterPriority wanted)
    {
        lock (_gate)
        {
            if (_held.TryGetValue(resource, out var current) && current == wanted)
            {
                return;
            }

            _held[resource] = wanted;
        }

        // A posse é por escritor e prioridade: para mudar de camada, solta e o próximo pedido pega de novo.
        _controls.Release(resource, Writer);
    }

    private static ProfileApplyResult Summarize(string profileId, List<SettingResult> results, bool stopped, IFormatProvider provider)
    {
        var result = new ProfileApplyResult(profileId, ProfileOutcome.Applied, string.Empty, results);
        var parts = new List<string>();
        if (result.AppliedCount > 0)
        {
            parts.Add(string.Create(provider, $"{result.AppliedCount} ajuste(s) aplicados"));
        }

        if (result.AwaitingCount > 0)
        {
            parts.Add(string.Create(provider, $"{result.AwaitingCount} awaiting confirmation"));
        }

        if (result.FailedCount > 0)
        {
            parts.Add(string.Create(provider, $"{result.FailedCount} failed"));
        }

        if (result.SkippedCount > 0)
        {
            parts.Add(string.Create(provider, $"{result.SkippedCount} skipped or blocked"));
        }

        var outcome = stopped ? ProfileOutcome.Superseded
            : results.Count == 0 || (result.FailedCount == 0 && result.SkippedCount == 0) ? ProfileOutcome.Applied
            : result.AppliedCount + result.AwaitingCount == 0 && result.FailedCount > 0 ? ProfileOutcome.Failed
            : ProfileOutcome.Partial;
        var message = parts.Count == 0 ? "Nothing to change." : string.Join(", ", parts) + ".";
        if (stopped)
        {
            message = "Interrupted by a newer profile switch. " + message;
        }

        return result with { Outcome = outcome, Message = message };
    }

    private void OnJournalChanged(object? sender, JournalEntry entry)
    {
        if (!entry.Completed || string.Equals(entry.Writer, Writer, StringComparison.Ordinal)
            || entry.Outcome is not (CommandOutcome.Verified or CommandOutcome.Applied or CommandOutcome.AwaitingConfirmation))
        {
            return;
        }

        string? owner;
        lock (_gate)
        {
            owner = _resolved.FirstOrDefault(r => r.Key == entry.Key)?.ProfileId;
            if (owner is null)
            {
                return;
            }

            _overrides[entry.Key] = entry.Writer;
        }

        Move(owner, ProfileState.Overridden, $"{entry.Label} mudado por {entry.Writer}");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ReleaseOverrides(IEnumerable<ControlKey> keys)
    {
        List<(ControlKey Key, string Writer)> released = [];
        lock (_gate)
        {
            foreach (var key in keys)
            {
                if (_overrides.Remove(key, out var writer))
                {
                    released.Add((key, writer));
                }
            }
        }

        foreach (var (key, writer) in released)
        {
            _controls.Release(key.Resource, writer);
        }
    }

    private void Move(string profileId, ProfileState state, string? reason)
    {
        StateMachine<ProfileState> machine;
        lock (_gate)
        {
            if (!_states.TryGetValue(profileId, out machine!))
            {
                _states[profileId] = machine = new StateMachine<ProfileState>(ProfileState.Inactive, StateModels.Profile, _time);
            }
        }

        if (!machine.TryMove(state, reason))
        {
            // Caminho não previsto na tabela (ex.: erro → substituído): passa pelo inativo, que liga todos.
            machine.TryMove(ProfileState.Inactive, reason);
            machine.TryMove(state, reason);
        }
    }

    private ProfileApplyResult Record(Profile profile, bool activated, ActivationCause cause, ProfileApplyResult result) =>
        Record(profile.Id, profile.Name, profile.Layer, activated, cause, result);

    private ProfileApplyResult Record(string id, string name, ProfileLayer layer, bool activated, ActivationCause cause, ProfileApplyResult result)
    {
        var record = new ProfileActivationRecord(_time.GetUtcNow(), id, name, layer, activated, cause, result);
        lock (_gate)
        {
            _history.AddFirst(record);
            while (_history.Count > HistoryCapacity)
            {
                _history.RemoveLast();
            }
        }

        Activated?.Invoke(this, record);
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    private static bool SameSlot(Profile a, Profile b) =>
        a.Layer == b.Layer && (a.Layer != ProfileLayer.Device || string.Equals(a.DeviceId, b.DeviceId, StringComparison.Ordinal));
}
