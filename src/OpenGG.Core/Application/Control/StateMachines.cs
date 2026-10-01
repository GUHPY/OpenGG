namespace OneRGB.Application.Control;

/// <summary>Estados de um recurso de hardware (A17). Todo adapter usa este vocabulário.</summary>
public enum FeatureState
{
    Unavailable,
    Discovered,
    Initializing,
    Ready,
    Applying,
    Applied,
    Verification,
    Error,
    Recovering,
    Disconnected,
}

/// <summary>Estados de um perfil (A17).</summary>
public enum ProfileState
{
    Inactive,
    Pending,
    Applying,
    Active,
    Overridden,
    Superseded,
    Error,
}

/// <summary>Estados de uma atualização de firmware (A17).</summary>
public enum FirmwareState
{
    Detected,
    Downloaded,
    Verified,
    Ready,
    UserApproved,
    Applying,
    Rebooting,
    Verifying,
    Complete,
    Failed,
    Rollback,
}

public sealed record StateChange<TState>(TState From, TState To, DateTimeOffset Time, string? Reason)
    where TState : struct, Enum;

/// <summary>
/// Máquina de estados com transições explícitas. Uma transição fora da tabela é um erro de programação e é
/// recusada — assim nenhum adapter inventa semântica própria (A17).
/// </summary>
public sealed class StateMachine<TState>
    where TState : struct, Enum
{
    private const int HistoryCapacity = 64;
    private readonly IReadOnlyDictionary<TState, IReadOnlyList<TState>> _transitions;
    private readonly TimeProvider _time;
    private readonly Queue<StateChange<TState>> _history = new();
    private readonly Lock _gate = new();
    private TState _state;

    public StateMachine(TState initial, IReadOnlyDictionary<TState, IReadOnlyList<TState>> transitions, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        _state = initial;
        _transitions = transitions;
        _time = time ?? TimeProvider.System;
    }

    public event EventHandler<StateChange<TState>>? Changed;

    public TState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public IReadOnlyList<StateChange<TState>> History
    {
        get
        {
            lock (_gate)
            {
                return [.. _history];
            }
        }
    }

    public bool CanMove(TState to)
    {
        lock (_gate)
        {
            return Allowed(_state, to);
        }
    }

    /// <summary>Move se a transição é permitida; ficar no mesmo estado é sempre permitido e não gera evento.</summary>
    public bool TryMove(TState to, string? reason = null)
    {
        StateChange<TState> change;
        lock (_gate)
        {
            if (EqualityComparer<TState>.Default.Equals(_state, to))
            {
                return true;
            }

            if (!Allowed(_state, to))
            {
                return false;
            }

            change = new StateChange<TState>(_state, to, _time.GetUtcNow(), reason);
            _state = to;
            _history.Enqueue(change);
            while (_history.Count > HistoryCapacity)
            {
                _history.Dequeue();
            }
        }

        Changed?.Invoke(this, change);
        return true;
    }

    public void Move(TState to, string? reason = null)
    {
        if (!TryMove(to, reason))
        {
            throw new InvalidOperationException($"Invalid transition: {State} → {to}.");
        }
    }

    private bool Allowed(TState from, TState to) =>
        _transitions.TryGetValue(from, out var targets) && targets.Contains(to);
}

/// <summary>Tabelas de transição comuns (A17).</summary>
public static class StateModels
{
    public static IReadOnlyDictionary<FeatureState, IReadOnlyList<FeatureState>> Feature { get; } = new Dictionary<FeatureState, IReadOnlyList<FeatureState>>
    {
        [FeatureState.Unavailable] = [FeatureState.Discovered, FeatureState.Disconnected],
        [FeatureState.Discovered] = [FeatureState.Initializing, FeatureState.Ready, FeatureState.Error, FeatureState.Disconnected, FeatureState.Unavailable],
        [FeatureState.Initializing] = [FeatureState.Ready, FeatureState.Error, FeatureState.Disconnected],
        [FeatureState.Ready] = [FeatureState.Applying, FeatureState.Error, FeatureState.Disconnected, FeatureState.Unavailable],
        [FeatureState.Applying] = [FeatureState.Verification, FeatureState.Applied, FeatureState.Error, FeatureState.Disconnected],
        [FeatureState.Verification] = [FeatureState.Applied, FeatureState.Error, FeatureState.Recovering, FeatureState.Disconnected],
        [FeatureState.Applied] = [FeatureState.Applying, FeatureState.Ready, FeatureState.Error, FeatureState.Disconnected, FeatureState.Unavailable],
        [FeatureState.Error] = [FeatureState.Recovering, FeatureState.Applying, FeatureState.Ready, FeatureState.Disconnected, FeatureState.Unavailable],
        [FeatureState.Recovering] = [FeatureState.Ready, FeatureState.Applied, FeatureState.Error, FeatureState.Disconnected],
        [FeatureState.Disconnected] = [FeatureState.Discovered, FeatureState.Initializing, FeatureState.Ready, FeatureState.Unavailable],
    };

    public static IReadOnlyDictionary<ProfileState, IReadOnlyList<ProfileState>> Profile { get; } = new Dictionary<ProfileState, IReadOnlyList<ProfileState>>
    {
        [ProfileState.Inactive] = [ProfileState.Pending, ProfileState.Applying],
        [ProfileState.Pending] = [ProfileState.Applying, ProfileState.Inactive, ProfileState.Superseded],
        [ProfileState.Applying] = [ProfileState.Active, ProfileState.Error, ProfileState.Superseded],
        [ProfileState.Active] = [ProfileState.Overridden, ProfileState.Superseded, ProfileState.Inactive, ProfileState.Applying, ProfileState.Error],
        [ProfileState.Overridden] = [ProfileState.Active, ProfileState.Applying, ProfileState.Superseded, ProfileState.Inactive],
        [ProfileState.Superseded] = [ProfileState.Inactive, ProfileState.Pending, ProfileState.Applying],
        [ProfileState.Error] = [ProfileState.Inactive, ProfileState.Pending, ProfileState.Applying],
    };

    public static IReadOnlyDictionary<FirmwareState, IReadOnlyList<FirmwareState>> Firmware { get; } = new Dictionary<FirmwareState, IReadOnlyList<FirmwareState>>
    {
        [FirmwareState.Detected] = [FirmwareState.Downloaded, FirmwareState.Failed],
        [FirmwareState.Downloaded] = [FirmwareState.Verified, FirmwareState.Failed],
        [FirmwareState.Verified] = [FirmwareState.Ready, FirmwareState.Failed],
        [FirmwareState.Ready] = [FirmwareState.UserApproved, FirmwareState.Detected],
        [FirmwareState.UserApproved] = [FirmwareState.Applying, FirmwareState.Ready],
        [FirmwareState.Applying] = [FirmwareState.Rebooting, FirmwareState.Verifying, FirmwareState.Failed],
        [FirmwareState.Rebooting] = [FirmwareState.Verifying, FirmwareState.Failed],
        [FirmwareState.Verifying] = [FirmwareState.Complete, FirmwareState.Failed],
        [FirmwareState.Failed] = [FirmwareState.Rollback, FirmwareState.Detected],
        [FirmwareState.Rollback] = [FirmwareState.Verifying, FirmwareState.Failed, FirmwareState.Complete],
        [FirmwareState.Complete] = [FirmwareState.Detected],
    };

    public static string Text(FeatureState state) => state switch
    {
        FeatureState.Unavailable => "Unavailable",
        FeatureState.Discovered => "Found",
        FeatureState.Initializing => "Iniciando",
        FeatureState.Ready => "Ready",
        FeatureState.Applying => "Applying",
        FeatureState.Applied => "Applied",
        FeatureState.Verification => "Awaiting confirmation",
        FeatureState.Error => "Error",
        FeatureState.Recovering => "Recuperando",
        _ => "Disconnected",
    };

    public static string Text(ProfileState state) => state switch
    {
        ProfileState.Inactive => "Inativo",
        ProfileState.Pending => "Na fila",
        ProfileState.Applying => "Applying",
        ProfileState.Active => "Ativo",
        ProfileState.Overridden => "Active with manual changes",
        ProfileState.Superseded => "Superseded",
        _ => "Error",
    };
}
