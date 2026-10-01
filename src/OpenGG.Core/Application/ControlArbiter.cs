using System.Text.Json.Serialization;
using OneRGB.Application.Control;

namespace OneRGB.Application;

/// <summary>Recurso de hardware disputável (ex.: <c>gpu-msi-rtx5070-gaming-trio/rgb</c>).</summary>
[JsonConverter(typeof(ResourceIdJsonConverter))]
public readonly record struct ResourceId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// Precedência de escritores (spec §23). Valor maior vence. Segurança sempre vence e não pode ser negada.
/// </summary>
public enum WriterPriority
{
    FactoryDefault = 0,
    GlobalProfile = 10,
    DeviceProfile = 20,
    ApplicationProfile = 30,

    /// <summary>
    /// Regra de automação mudando um controle direto (A32): acima dos perfis, que ela ajusta por um tempo, e abaixo
    /// do ajuste manual — o usuário sempre pode mexer por cima de uma regra.
    /// </summary>
    Automation = 35,
    TemporaryUserOverride = 40,
    Safety = 100,
}

/// <summary>Posse concedida de um recurso. Só o dono da posse pode escrever.</summary>
public sealed record Lease(ResourceId Resource, string Writer, WriterPriority Priority, long Token);

public enum AcquireOutcome
{
    Granted,
    Preempted,
    RejectedLowerOrEqualPriority,
    RejectedExternalOwner,
}

public sealed record AcquireResult(AcquireOutcome Outcome, Lease? Lease, string? CurrentOwner)
{
    public bool Succeeded => Outcome is AcquireOutcome.Granted or AcquireOutcome.Preempted;
}

public sealed record OwnershipChange(ResourceId Resource, string? PreviousOwner, string? NewOwner);

/// <summary>
/// Árbitro central de controle (spec §24, §93, §141). Garante um único escritor por recurso,
/// impede que escritores de mesma prioridade "briguem" e respeita apps de fabricante detectados
/// como donos externos (modo de coexistência, spec §196).
/// </summary>
public sealed class ControlArbiter
{
    private readonly Lock _gate = new();
    private readonly Dictionary<ResourceId, Lease> _owners = [];
    private readonly Dictionary<ResourceId, string> _external = [];
    private long _nextToken;

    public event EventHandler<OwnershipChange>? OwnershipChanged;

    public AcquireResult TryAcquire(ResourceId resource, string writer, WriterPriority priority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(writer);
        OwnershipChange? change = null;
        AcquireResult result;

        lock (_gate)
        {
            if (_external.TryGetValue(resource, out var externalOwner) && priority != WriterPriority.Safety)
            {
                return new AcquireResult(AcquireOutcome.RejectedExternalOwner, null, externalOwner);
            }

            if (_owners.TryGetValue(resource, out var current))
            {
                if (string.Equals(current.Writer, writer, StringComparison.Ordinal) && current.Priority == priority)
                {
                    return new AcquireResult(AcquireOutcome.Granted, current, current.Writer);
                }

                if (priority <= current.Priority)
                {
                    return new AcquireResult(AcquireOutcome.RejectedLowerOrEqualPriority, null, current.Writer);
                }

                var lease = NewLease(resource, writer, priority);
                _owners[resource] = lease;
                change = new OwnershipChange(resource, current.Writer, writer);
                result = new AcquireResult(AcquireOutcome.Preempted, lease, current.Writer);
            }
            else
            {
                var lease = NewLease(resource, writer, priority);
                _owners[resource] = lease;
                change = new OwnershipChange(resource, null, writer);
                result = new AcquireResult(AcquireOutcome.Granted, lease, null);
            }
        }

        OwnershipChanged?.Invoke(this, change);
        return result;
    }

    /// <summary>Verdadeiro só se a posse ainda é a vigente (uma posse preemptada deixa de valer).</summary>
    public bool IsCurrent(Lease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (_gate)
        {
            return _owners.TryGetValue(lease.Resource, out var current) && current.Token == lease.Token;
        }
    }

    public bool Release(Lease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (_gate)
        {
            if (!_owners.TryGetValue(lease.Resource, out var current) || current.Token != lease.Token)
            {
                return false;
            }

            _owners.Remove(lease.Resource);
        }

        OwnershipChanged?.Invoke(this, new OwnershipChange(lease.Resource, lease.Writer, null));
        return true;
    }

    /// <summary>Solta a posse de um escritor sem ter o objeto da posse (fim de ajuste manual, perfil desativado).</summary>
    public bool ReleaseWriter(ResourceId resource, string writer)
    {
        lock (_gate)
        {
            if (!_owners.TryGetValue(resource, out var current) || !string.Equals(current.Writer, writer, StringComparison.Ordinal))
            {
                return false;
            }

            _owners.Remove(resource);
        }

        OwnershipChanged?.Invoke(this, new OwnershipChange(resource, writer, null));
        return true;
    }

    /// <summary>
    /// Registra que um app de fabricante controla o recurso (spec §92, §197). As posses do OneRGB, exceto
    /// Safety, são revogadas para não disputar com ele.
    /// </summary>
    public void MarkExternallyOwned(ResourceId resource, string externalApp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalApp);
        OwnershipChange? change = null;
        lock (_gate)
        {
            _external[resource] = externalApp;
            if (_owners.TryGetValue(resource, out var current) && current.Priority != WriterPriority.Safety)
            {
                _owners.Remove(resource);
                change = new OwnershipChange(resource, current.Writer, externalApp);
            }
        }

        if (change is not null)
        {
            OwnershipChanged?.Invoke(this, change);
        }
    }

    public void ClearExternalOwner(ResourceId resource)
    {
        lock (_gate)
        {
            _external.Remove(resource);
        }
    }

    public string? OwnerOf(ResourceId resource)
    {
        lock (_gate)
        {
            if (_external.TryGetValue(resource, out var external))
            {
                return _owners.TryGetValue(resource, out var safety) ? safety.Writer : external;
            }

            return _owners.TryGetValue(resource, out var current) ? current.Writer : null;
        }
    }

    /// <summary>
    /// App de fabricante que controla o recurso agora; nulo se nenhum. Quem envia continuamente (quadros de RGB) confere
    /// isto a cada envio para parar assim que o app abre.
    /// </summary>
    public string? ExternalOwnerOf(ResourceId resource)
    {
        lock (_gate)
        {
            return _external.GetValueOrDefault(resource);
        }
    }

    /// <summary>Recursos com dono externo agora e o app de cada um.</summary>
    public IReadOnlyDictionary<ResourceId, string> ExternalOwners()
    {
        lock (_gate)
        {
            return new Dictionary<ResourceId, string>(_external);
        }
    }

    private Lease NewLease(ResourceId resource, string writer, WriterPriority priority) =>
        new(resource, writer, priority, ++_nextToken);
}
