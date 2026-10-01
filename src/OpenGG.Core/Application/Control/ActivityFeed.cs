namespace OneRGB.Application.Control;

/// <summary>Estado de uma escrita na central de operações.</summary>
public enum ActivityState
{
    /// <summary>Pedido enviado, esperando o dispositivo responder.</summary>
    Running,

    /// <summary>Mudança de alto risco aplicada, esperando o "manter" dentro do prazo (A28).</summary>
    AwaitingConfirmation,

    /// <summary>O OneRGB fechou no meio da escrita (ou antes da confirmação): conferir o dispositivo.</summary>
    Interrupted,

    Succeeded,

    /// <summary>Falhou, foi recusada pela validação ou bloqueada pelo árbitro.</summary>
    Failed,

    /// <summary>Voltou ao valor anterior (lote com falha, confirmação recusada ou vencida).</summary>
    Reverted,

    /// <summary>Substituída por um pedido mais novo ou cancelada antes de sair.</summary>
    Skipped,
}

/// <summary>Linha da central de operações (spec §408, §409): o quê, onde, estado, mudança, motivo e resultado.</summary>
public sealed record ActivityItem
{
    public required string OperationId { get; init; }

    public required ControlKey Key { get; init; }

    public required string Title { get; init; }

    /// <summary>Dispositivo dono do controle.</summary>
    public required string Target { get; init; }

    public required ActivityState State { get; init; }

    public required string StateText { get; init; }

    /// <summary>"80 % → 40 %" (antes → pedido ou obtido); nulo sem valores legíveis.</summary>
    public string? Change { get; init; }

    public string Message { get; init; } = string.Empty;

    /// <summary>Por que a escrita aconteceu (perfil, regra, "Desfazer").</summary>
    public string? Reason { get; init; }

    public string Writer { get; init; } = string.Empty;

    public required DateTimeOffset Time { get; init; }

    public TimeSpan Duration { get; init; }

    /// <summary>Prazo da confirmação, enquanto ela está aberta.</summary>
    public DateTimeOffset? ConfirmBy { get; init; }

    public RiskLevel Risk { get; init; }

    public bool Reversible { get; init; }

    public bool NeedsAttention => State is ActivityState.AwaitingConfirmation or ActivityState.Interrupted or ActivityState.Failed;
}

/// <summary>Contagem para a barra de status e o selo da central.</summary>
public sealed record ActivitySummary(int Running, int AwaitingConfirmation, int Interrupted, int Failed)
{
    public static ActivitySummary Empty { get; } = new(0, 0, 0, 0);

    public int Attention => AwaitingConfirmation + Interrupted + Failed;

    /// <summary>O mais urgente primeiro; nulo quando não há nada em andamento nem pendente.</summary>
    public string? Text
    {
        get
        {
            if (AwaitingConfirmation > 0)
            {
                return AwaitingConfirmation == 1 ? "Confirm 1 change" : $"Confirm {AwaitingConfirmation} changes";
            }

            if (Running > 0)
            {
                return Running == 1 ? "1 write in progress" : $"{Running} writes in progress";
            }

            if (Interrupted > 0)
            {
                return Interrupted == 1 ? "1 escrita interrompida" : $"{Interrupted} escritas interrompidas";
            }

            if (Failed > 0)
            {
                return Failed == 1 ? "1 write failed" : $"{Failed} writes failed";
            }

            return null;
        }
    }
}

/// <summary>
/// Monta a central de operações a partir do jornal (ADR-0005) e do estado vivo dos controles: o que está em
/// andamento, o que espera confirmação, o que a última execução deixou pela metade e o histórico recente.
/// </summary>
public static class ActivityFeed
{
    /// <summary>Mensagem que o host grava ao fechar escritas deixadas pela metade (A33).</summary>
    public const string InterruptedPrefix = "Interrompida:";

    public static IReadOnlyList<ActivityItem> Build(
        IEnumerable<JournalEntry> recent,
        IEnumerable<ControlStatus> statuses,
        IReadOnlySet<string> interrupted,
        Func<ResourceId, string?> deviceName,
        IFormatProvider provider,
        int maximum = 200)
    {
        ArgumentNullException.ThrowIfNull(recent);
        ArgumentNullException.ThrowIfNull(statuses);
        ArgumentNullException.ThrowIfNull(interrupted);
        ArgumentNullException.ThrowIfNull(deviceName);
        ArgumentNullException.ThrowIfNull(provider);
        var live = statuses.ToDictionary(s => s.Key);
        var items = new List<ActivityItem>();
        foreach (var entry in recent)
        {
            if (items.Count >= maximum)
            {
                break;
            }

            live.TryGetValue(entry.Key, out var status);
            var confirmBy = entry.Outcome == CommandOutcome.AwaitingConfirmation ? status?.ConfirmBy : null;
            var state = StateOf(entry, interrupted.Contains(entry.OperationId), confirmBy is not null);
            var spec = status?.Spec ?? new ControlSpec { Setting = entry.Key.Setting, Label = entry.Label };
            items.Add(new ActivityItem
            {
                OperationId = entry.OperationId,
                Key = entry.Key,
                Title = entry.Label,
                Target = deviceName(entry.Key.Resource) ?? entry.Key.Resource.Value,
                State = state,
                StateText = StateText(state, entry.Outcome),
                Change = Change(entry, spec, provider),
                Message = entry.Message,
                Reason = entry.Reason,
                Writer = entry.Writer,
                Time = entry.Time,
                Duration = entry.Duration,
                ConfirmBy = confirmBy,
                Risk = entry.Risk,
                Reversible = entry.Reversible,
            });
        }

        return items;
    }

    public static ActivitySummary Summarize(IEnumerable<ActivityItem> items, DateTimeOffset failuresSince)
    {
        ArgumentNullException.ThrowIfNull(items);
        int running = 0, awaiting = 0, interrupted = 0, failed = 0;
        foreach (var item in items)
        {
            switch (item.State)
            {
                case ActivityState.Running:
                    running++;
                    break;
                case ActivityState.AwaitingConfirmation:
                    awaiting++;
                    break;
                case ActivityState.Interrupted:
                    interrupted++;
                    break;
                case ActivityState.Failed when item.Time >= failuresSince:
                    failed++;
                    break;
            }
        }

        return new ActivitySummary(running, awaiting, interrupted, failed);
    }

    /// <summary>
    /// Estado de uma entrada. "Awaiting confirmation" sem prazo vivo quer dizer que o app fechou antes da resposta:
    /// conta como interrompida, porque ninguém garante em que valor o dispositivo ficou.
    /// </summary>
    public static ActivityState StateOf(JournalEntry entry, bool interrupted, bool confirmationOpen)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (interrupted)
        {
            return ActivityState.Interrupted;
        }

        return entry.Outcome switch
        {
            null => ActivityState.Running,
            CommandOutcome.Verified or CommandOutcome.Applied => ActivityState.Succeeded,
            CommandOutcome.AwaitingConfirmation => confirmationOpen ? ActivityState.AwaitingConfirmation : ActivityState.Interrupted,
            CommandOutcome.RolledBack => ActivityState.Reverted,
            CommandOutcome.Superseded or CommandOutcome.Cancelled => ActivityState.Skipped,
            _ => ActivityState.Failed,
        };
    }

    public static string StateText(ActivityState state, CommandOutcome? outcome) => state switch
    {
        ActivityState.Running => "Em andamento",
        ActivityState.AwaitingConfirmation => "Awaiting your confirmation",
        ActivityState.Interrupted => outcome == CommandOutcome.AwaitingConfirmation ? "Unconfirmed: OpenGG closed before confirmation" : "Interrompida",
        _ => OperationJournal.OutcomeText(outcome),
    };

    private static string? Change(JournalEntry entry, ControlSpec spec, IFormatProvider provider)
    {
        var target = entry.After ?? entry.Requested;
        if (target is null)
        {
            return null;
        }

        var to = target.Display(spec, provider);
        return entry.Before is { } before ? $"{before.Display(spec, provider)} → {to}" : to;
    }
}
