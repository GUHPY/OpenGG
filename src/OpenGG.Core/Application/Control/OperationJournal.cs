using System.Text.Json.Serialization;

namespace OneRGB.Application.Control;

/// <summary>Nível da trilha de auditoria (spec §336).</summary>
public enum AuditLevel
{
    Disabled,

    /// <summary>Só falhas, recusas, reversões e mudanças de alto risco.</summary>
    Minimal,

    /// <summary>Toda escrita em hardware.</summary>
    Standard,

    /// <summary>Também pedidos substituídos e leituras de verificação.</summary>
    Extended,
}

/// <summary>
/// Registro de uma operação. É criado ANTES da escrita com o estado anterior (ADR-0005) e completado depois;
/// uma entrada sem desfecho após reiniciar indica escrita interrompida (A33).
/// </summary>
public sealed record JournalEntry
{
    public required long Sequence { get; init; }

    public required string OperationId { get; init; }

    /// <summary>Hora de parede (UTC) para o usuário; a duração usa relógio monotônico (spec §337).</summary>
    public required DateTimeOffset Time { get; init; }

    public required ControlKey Key { get; init; }

    public required string Label { get; init; }

    public required string Writer { get; init; }

    public WriterPriority Priority { get; init; }

    public RiskLevel Risk { get; init; }

    public ControlValue? Before { get; init; }

    public ControlValue? Requested { get; init; }

    public ControlValue? After { get; init; }

    /// <summary><c>null</c> enquanto a operação está em andamento.</summary>
    public CommandOutcome? Outcome { get; init; }

    public string Message { get; init; } = string.Empty;

    /// <summary>Por que a escrita aconteceu ("Perfil 'Competitivo' — valorant.exe em primeiro plano", "Desfazer").</summary>
    public string? Reason { get; init; }

    public TimeSpan Duration { get; init; }

    public bool Reversible { get; init; }

    [JsonIgnore]
    public bool Completed => Outcome is not null;
}

/// <summary>
/// Jornal de operações em memória (limitado) com evento para persistência. Fonte de "último escritor" e
/// "última escrita" do modelo de posse (A15.5) e da tela de atividade.
/// </summary>
public sealed class OperationJournal(int capacity = 2000)
{
    private readonly Lock _gate = new();
    private readonly LinkedList<JournalEntry> _entries = new();
    private readonly Dictionary<long, LinkedListNode<JournalEntry>> _bySequence = [];

    // Última escrita por recurso (nula = nenhuma), refeita só quando uma entrada do recurso muda ou sai do jornal.
    private readonly Dictionary<ResourceId, JournalEntry?> _lastWrite = [];
    private long _sequence;

    /// <summary>Disparado quando uma entrada é criada ou completada (e deve ser gravada, conforme o nível).</summary>
    public event EventHandler<JournalEntry>? Changed;

    public AuditLevel Level { get; set; } = AuditLevel.Standard;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public JournalEntry Begin(string operationId, ControlKey key, string label, string writer, WriterPriority priority, RiskLevel risk, ControlValue? before, ControlValue requested, bool reversible, DateTimeOffset time, string? reason = null)
    {
        JournalEntry entry;
        lock (_gate)
        {
            entry = new JournalEntry
            {
                Sequence = ++_sequence,
                OperationId = operationId,
                Time = time,
                Key = key,
                Label = label,
                Writer = writer,
                Priority = priority,
                Risk = risk,
                Before = before,
                Requested = requested,
                Reversible = reversible,
                Reason = reason,
            };
            _bySequence[entry.Sequence] = _entries.AddLast(entry);
            Trim();
        }

        Changed?.Invoke(this, entry);
        return entry;
    }

    public JournalEntry Complete(JournalEntry pending, CommandOutcome outcome, ControlValue? after, string message, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(pending);
        var done = pending with { Outcome = outcome, After = after, Message = message, Duration = duration };
        lock (_gate)
        {
            if (_bySequence.TryGetValue(pending.Sequence, out var node))
            {
                node.Value = done;
                _lastWrite.Remove(done.Key.Resource);
            }
        }

        Changed?.Invoke(this, done);
        return done;
    }

    /// <summary>Recarrega entradas gravadas (a sequência continua de onde parou).</summary>
    public void Load(IEnumerable<JournalEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        lock (_gate)
        {
            foreach (var entry in entries.OrderBy(e => e.Sequence))
            {
                if (_bySequence.ContainsKey(entry.Sequence))
                {
                    continue;
                }

                _bySequence[entry.Sequence] = _entries.AddLast(entry);
                _lastWrite.Remove(entry.Key.Resource);
                _sequence = Math.Max(_sequence, entry.Sequence);
            }

            Trim();
        }
    }

    /// <summary>Entradas mais recentes primeiro.</summary>
    public IReadOnlyList<JournalEntry> Recent(int maximum = int.MaxValue)
    {
        lock (_gate)
        {
            var result = new List<JournalEntry>(Math.Min(maximum, _entries.Count));
            for (var node = _entries.Last; node is not null && result.Count < maximum; node = node.Previous)
            {
                result.Add(node.Value);
            }

            return result;
        }
    }

    public IReadOnlyList<JournalEntry> For(ResourceId resource, int maximum = 50) => Newest(e => e.Key.Resource == resource, maximum);

    /// <summary>
    /// Última escrita concluída com sucesso no recurso. O estado de cada controle pergunta isto a cada evento, e procurar
    /// nas 2000 entradas por controle travava a UI na abertura (centenas de eventos seguidos): fica em cache.
    /// </summary>
    public JournalEntry? LastWrite(ResourceId resource)
    {
        lock (_gate)
        {
            if (!_lastWrite.TryGetValue(resource, out var last))
            {
                for (var node = _entries.Last; node is not null && last is null; node = node.Previous)
                {
                    if (node.Value.Key.Resource == resource && node.Value.Outcome is CommandOutcome.Verified or CommandOutcome.Applied or CommandOutcome.AwaitingConfirmation)
                    {
                        last = node.Value;
                    }
                }

                _lastWrite[resource] = last;
            }

            return last;
        }
    }

    /// <summary>Operações sem desfecho (o app caiu no meio de uma escrita) — o painel de saúde oferece reparo.</summary>
    public IReadOnlyList<JournalEntry> Interrupted() => Newest(e => !e.Completed, int.MaxValue);

    /// <summary>Mais recentes primeiro, sem copiar o jornal inteiro.</summary>
    private List<JournalEntry> Newest(Func<JournalEntry, bool> match, int maximum)
    {
        var result = new List<JournalEntry>();
        lock (_gate)
        {
            for (var node = _entries.Last; node is not null && result.Count < maximum; node = node.Previous)
            {
                if (match(node.Value))
                {
                    result.Add(node.Value);
                }
            }
        }

        return result;
    }

    /// <summary>Esta entrada deve ir para o disco no nível atual (spec §336)?</summary>
    public bool ShouldPersist(JournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Level switch
        {
            AuditLevel.Disabled => false,
            AuditLevel.Minimal => entry.Risk == RiskLevel.High
                || entry.Outcome is CommandOutcome.Failed or CommandOutcome.Rejected or CommandOutcome.Blocked or CommandOutcome.RolledBack,
            AuditLevel.Standard => entry.Outcome is not CommandOutcome.Superseded,
            _ => true,
        };
    }

    public static TimeSpan RetentionFor(AuditLevel level) => level switch
    {
        AuditLevel.Disabled => TimeSpan.Zero,
        AuditLevel.Minimal => TimeSpan.FromDays(7),
        AuditLevel.Standard => TimeSpan.FromDays(30),
        _ => TimeSpan.FromDays(90),
    };

    public static string OutcomeText(CommandOutcome? outcome) => outcome switch
    {
        null => "Em andamento",
        CommandOutcome.Verified => "Aplicado e confirmado",
        CommandOutcome.Applied => "Applied (without readback)",
        CommandOutcome.AwaitingConfirmation => "Awaiting your confirmation",
        CommandOutcome.Superseded => "Superseded by a newer value",
        CommandOutcome.Rejected => "Recusado",
        CommandOutcome.Blocked => "Blocked",
        CommandOutcome.Failed => "Failed",
        CommandOutcome.RolledBack => "Reverted",
        _ => "Cancelled",
    };

    private void Trim()
    {
        while (_entries.Count > Math.Max(capacity, 1) && _entries.First is { } first)
        {
            _bySequence.Remove(first.Value.Sequence);
            _lastWrite.Remove(first.Value.Key.Resource);
            _entries.RemoveFirst();
        }
    }
}
