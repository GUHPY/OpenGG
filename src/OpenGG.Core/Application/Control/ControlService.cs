using System.Globalization;

namespace OneRGB.Application.Control;

/// <summary>
/// Caminho único de escrita em hardware (A18, ADR-0005):
/// <c>UI → Comando → Validação → Árbitro → Leitura (ponto seguro) → Jornal → Adapter → Verificação → Estado → UI</c>.
/// Dá retorno imediato (A27: o valor desejado aparece na hora como pendente), junta pedidos em sequência do mesmo
/// controle (o mais novo vence, sem fila de escritas velhas), desfaz/refaz, reverte e exige confirmação em
/// mudanças de alto risco com reversão automática (A28).
/// </summary>
public sealed class ControlService : IDisposable
{
    private const int UndoCapacity = 100;
    private readonly ControlArbiter _arbiter;
    private readonly OperationJournal _journal;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<ControlKey, Slot> _slots = [];
    private readonly LinkedList<UndoItem> _undo = new();
    private readonly Stack<UndoItem> _redo = new();

    public ControlService(ControlArbiter arbiter, OperationJournal journal, TimeProvider? time = null)
    {
        _arbiter = arbiter ?? throw new ArgumentNullException(nameof(arbiter));
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Estado de um controle mudou (pendente, aplicado, erro…). Pode vir de qualquer thread.</summary>
    public event EventHandler<ControlStatus>? StatusChanged;

    /// <summary>Uma operação terminou (para notificações e central de operações).</summary>
    public event EventHandler<CommandResult>? Executed;

    /// <summary>Um controle ficou disponível (primeiro registro ou reconexão): perfis reaplicam (A5.12).</summary>
    public event EventHandler<ControlKey>? Connected;

    public OperationJournal Journal => _journal;

    public bool CanUndo
    {
        get
        {
            lock (_gate)
            {
                return _undo.Count > 0;
            }
        }
    }

    public bool CanRedo
    {
        get
        {
            lock (_gate)
            {
                return _redo.Count > 0;
            }
        }
    }

    /// <summary>Texto do próximo "Desfazer" (ex.: "Desfazer Brilho: 80 %").</summary>
    public string? UndoLabel
    {
        get
        {
            lock (_gate)
            {
                return _undo.Last?.Value.Label;
            }
        }
    }

    /// <summary>Registra (ou troca, no hotplug) o adapter de um controle.</summary>
    public void Register(IControlAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        Slot slot;
        var arrived = false;
        lock (_gate)
        {
            if (_slots.TryGetValue(adapter.Key, out var existing))
            {
                existing.Adapter = adapter;
                slot = existing;
            }
            else
            {
                slot = new Slot(adapter, _time);
                _slots[adapter.Key] = slot;
                arrived = true;
            }
        }

        if (adapter.IsAvailable)
        {
            if (slot.State.State is FeatureState.Unavailable or FeatureState.Disconnected)
            {
                slot.State.TryMove(FeatureState.Discovered, "Device found");
                arrived = true;
            }

            slot.State.TryMove(FeatureState.Ready, "Adapter ready");
        }
        else
        {
            slot.State.TryMove(FeatureState.Unavailable, "Device unavailable");
            arrived = false;
        }

        Raise(slot);
        if (arrived)
        {
            Connected?.Invoke(this, adapter.Key);
        }
    }

    /// <summary>Dispositivo saiu (hotplug): o controle fica desconectado, com o último estado preservado.</summary>
    public bool Disconnect(ControlKey key)
    {
        Slot? slot;
        lock (_gate)
        {
            _slots.TryGetValue(key, out slot);
        }

        if (slot is null)
        {
            return false;
        }

        slot.State.TryMove(FeatureState.Disconnected, "Device disconnected");
        Raise(slot);
        return true;
    }

    public ControlStatus? Status(ControlKey key)
    {
        lock (_gate)
        {
            return _slots.TryGetValue(key, out var slot) ? ToStatus(slot) : null;
        }
    }

    public IReadOnlyList<ControlStatus> Statuses(ResourceId? resource = null)
    {
        lock (_gate)
        {
            return [.. _slots.Values.Where(s => resource is null || s.Adapter.Key.Resource == resource).Select(ToStatus)];
        }
    }

    /// <summary>Superfície de controle de um recurso, gerada por capacidade (A25).</summary>
    public IReadOnlyList<ControlSpec> Capabilities(ResourceId resource)
    {
        lock (_gate)
        {
            return [.. _slots.Values.Where(s => s.Adapter.Key.Resource == resource && s.Adapter.IsAvailable).Select(s => s.Adapter.Spec)];
        }
    }

    /// <summary>Outro perfil foi carregado no dispositivo: valores aplicados anteriormente deixam de ser válidos.</summary>
    public void InvalidateApplied(ResourceId resource)
    {
        List<Slot> slots;
        lock (_gate) { slots = [.. _slots.Values.Where(s => s.Adapter.Key.Resource == resource)]; }
        foreach (var slot in slots)
        {
            Interlocked.Increment(ref slot.Version);
            slot.Desired = slot.Applied = slot.Observed = null;
            slot.Message = null;
            Raise(slot);
        }
    }

    /// <summary>O perfil ativo informa o valor salvo (para mostrar "temporário" quando o aplicado difere).</summary>
    public void SetPersistent(ControlKey key, ControlValue? value)
    {
        Slot? slot;
        lock (_gate)
        {
            if (!_slots.TryGetValue(key, out slot))
            {
                return;
            }

            slot.Persistent = value;
        }

        Raise(slot);
    }

    /// <summary>Lê o valor atual do dispositivo (atualizar tela, hotplug, verificação manual).</summary>
    public async Task<ControlValue?> RefreshAsync(ControlKey key, CancellationToken cancellationToken = default)
    {
        var slot = Find(key);
        if (slot is null || !slot.Adapter.Spec.Readable)
        {
            return null;
        }

        if (!slot.Adapter.IsAvailable)
        {
            Raise(slot); // quem acompanha (bateria nos cards) fica sabendo que o dispositivo saiu
            return null;
        }

        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var observed = await ReadAsync(slot.Adapter, cancellationToken).ConfigureAwait(false);
            slot.Observed = observed ?? slot.Observed;
            return observed;
        }
#pragma warning disable CA1031 // Falha de leitura de um adapter vira mensagem no controle, não derruba a tela.
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            slot.Message = $"Read failed: {ex.Message}";
            if (!slot.Adapter.Spec.Writable)
            {
                slot.Observed = null; // estado informado (bateria, carregando): sem leitura agora, não vale o último
            }

            return null;
        }
        finally
        {
            slot.Gate.Release();
            Raise(slot);
        }
    }

    /// <summary>Validação sem escrever (A26: valor exato, passo, faixa e envelope de segurança).</summary>
    public string? Validate(ControlRequest request, out ControlValue normalized)
    {
        ArgumentNullException.ThrowIfNull(request);
        normalized = request.Value;
        var slot = Find(request.Key);
        return slot is null ? "Unknown control." : Validate(slot.Adapter.Spec, request, out normalized);
    }

    /// <summary>Prévia do que um conjunto de pedidos mudaria (ADR-0005 "draft/preview").</summary>
    public IReadOnlyList<PlannedChange> Preview(IEnumerable<ControlRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var result = new List<PlannedChange>();
        foreach (var request in requests)
        {
            var slot = Find(request.Key);
            if (slot is null)
            {
                result.Add(new PlannedChange(request.Key, request.Key.Setting, null, request.Value, RiskLevel.Safe, false, "Control not found on this computer."));
                continue;
            }

            var spec = slot.Adapter.Spec;
            var error = Validate(spec, request, out var value) ?? Gate(slot, request);
            var current = slot.Observed ?? slot.Applied;
            result.Add(new PlannedChange(request.Key, spec.Label, current, value, spec.Risk, current is null || !current.Matches(value, spec.Tolerance), error));
        }

        return result;
    }

    public Task<CommandResult> ExecuteAsync(ControlRequest request, CancellationToken cancellationToken = default) =>
        ExecuteCoreAsync(request, UndoMode.Record, cancellationToken);

    /// <summary>Como <see cref="ExecuteAsync"/>, fora do desfazer (autoteste de integração, que restaura sozinho).</summary>
    public Task<CommandResult> ExecuteWithoutUndoAsync(ControlRequest request, CancellationToken cancellationToken = default) =>
        ExecuteCoreAsync(request, UndoMode.Skip, cancellationToken);

    /// <summary>Somente ajustes seguros sem leitura e sem troca de recurso podem compartilhar uma escrita.</summary>
    public object? BatchGroup(ControlKey key) => Find(key) is { Adapter: var adapter }
        && adapter.Spec.Risk == RiskLevel.Safe && !adapter.Spec.Readable && !adapter.Spec.SwitchesResource
            ? adapter.BatchGroup : null;

    /// <summary>Valida todos, registra todos antes da E/S e confirma todos só depois do ACK do lote.</summary>
    public async Task<IReadOnlyList<CommandResult>> ExecuteBatchAsync(IReadOnlyList<ControlRequest> requests, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0) { return []; }
        var first = requests[0];
        var group = BatchGroup(first.Key);
        if (group is null || requests.Any(r => !ReferenceEquals(BatchGroup(r.Key), group) || r.Key.Resource != first.Key.Resource
            || r.Writer != first.Writer || r.Priority != first.Priority) || requests.Select(r => r.Key).Distinct().Count() != requests.Count)
        {
            throw new ArgumentException("A batch requires compatible, distinct controls from the same resource and writer.", nameof(requests));
        }

        var items = new List<(ControlRequest Request, Slot Slot, ControlValue Value, long Version)>();
        foreach (var request in requests)
        {
            var slot = Find(request.Key)!;
            var invalid = Validate(slot.Adapter.Spec, request, out var value);
            var refusal = invalid ?? Gate(slot, request) ?? (slot.Confirmation is not null ? "Confirm or revert the previous change first." : null);
            if (refusal is not null)
            {
                var outcome = invalid is null ? CommandOutcome.Blocked : CommandOutcome.Rejected;
                var entry = _journal.Begin(NewId(), request.Key, slot.Adapter.Spec.Label, request.Writer, request.Priority,
                    slot.Adapter.Spec.Risk, slot.Applied, value, false, _time.GetUtcNow(), request.Reason);
                _journal.Complete(entry, outcome, slot.Applied, refusal, TimeSpan.Zero);
                var refused = Finish(slot, new CommandResult(entry.OperationId, request.Key, outcome, slot.Applied, slot.Applied, refusal));
                return [.. requests.Select(r => r.Key == request.Key ? refused : new CommandResult(NewId(), r.Key,
                    CommandOutcome.Blocked, Find(r.Key)?.Applied, Find(r.Key)?.Applied, "Batch not applied: another setting was rejected."))];
            }
            items.Add((request, slot, value, 0));
        }
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            items[i] = (item.Request, item.Slot, item.Value, Interlocked.Increment(ref item.Slot.Version));
            item.Slot.Desired = item.Value;
            Raise(item.Slot);
        }

        var locked = new List<Slot>();
        var entries = new List<(ControlRequest Request, Slot Slot, ControlValue Value, JournalEntry Entry)>();
        var started = _time.GetTimestamp();
        try
        {
            foreach (var item in items.OrderBy(i => i.Request.Key.ToString(), StringComparer.Ordinal))
            {
                await item.Slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                locked.Add(item.Slot);
            }
            if (items.Any(i => i.Version != Volatile.Read(ref i.Slot.Version)))
            {
                return [.. requests.Select(r => new CommandResult(NewId(), r.Key, CommandOutcome.Superseded, null, null, "Batch superseded by a newer change."))];
            }
            var acquired = _arbiter.TryAcquire(first.Key.Resource, first.Writer, first.Priority);
            var reason = items.Select(i => Gate(i.Slot, i.Request)).FirstOrDefault(r => r is not null);
            if (reason is not null || acquired.Lease is null)
            {
                return [.. items.Select(i =>
                {
                    i.Slot.Desired = i.Slot.Applied;
                    var message = reason ?? $"Blocked: {acquired.CurrentOwner} controls this resource.";
                    var entry = _journal.Begin(NewId(), i.Request.Key, i.Slot.Adapter.Spec.Label, i.Request.Writer, i.Request.Priority,
                        i.Slot.Adapter.Spec.Risk, i.Slot.Applied, i.Value, false, _time.GetUtcNow(), i.Request.Reason);
                    _journal.Complete(entry, CommandOutcome.Blocked, i.Slot.Applied, message, TimeSpan.Zero);
                    return Finish(i.Slot, new CommandResult(entry.OperationId, i.Request.Key, CommandOutcome.Blocked,
                        i.Slot.Applied, i.Slot.Applied, message));
                })];
            }

            foreach (var (request, slot, value, _) in items)
            {
                var entry = _journal.Begin(NewId(), request.Key, slot.Adapter.Spec.Label, request.Writer, request.Priority,
                    slot.Adapter.Spec.Risk, slot.Applied, value, slot.Adapter.Spec.Reversible && slot.Applied is not null, _time.GetUtcNow(), request.Reason);
                entries.Add((request, slot, value, entry));
                slot.State.TryMove(FeatureState.Applying, request.Reason ?? "Aplicando lote");
                Raise(slot);
            }
            var interval = items.Max(i => i.Slot.Adapter.Spec.Timeout);
            using var timeout = new CancellationTokenSource(interval, _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                await items[0].Slot.Adapter.WriteBatchAsync(items.ToDictionary(i => i.Request.Key, i => i.Value), acquired.Lease, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"The device did not respond within {interval.TotalSeconds:0.#} s.");
            }

            var results = new List<CommandResult>();
            foreach (var (request, slot, value, entry) in entries)
            {
                if (slot.Adapter.Spec.Reversible && entry.Before is { } before && !before.Matches(value, slot.Adapter.Spec.Tolerance))
                {
                    lock (_gate) { PushUndo(new UndoItem(request.Key, before, value, slot.Adapter.Spec.Label, request.Writer, request.Priority)); _redo.Clear(); }
                }
                slot.Applied = value;
                slot.Message = null;
                slot.State.TryMove(FeatureState.Applied, "Batch accepted by the device");
                const string message = "Applied in a batch; the device accepted the command (without live value readback).";
                _journal.Complete(entry, CommandOutcome.Applied, value, message, _time.GetElapsedTime(started));
                results.Add(Finish(slot, new CommandResult(entry.OperationId, request.Key, CommandOutcome.Applied, entry.Before, value, message)));
            }
            return results;
        }
#pragma warning disable CA1031 // Fronteira com hardware, como WriteAsync: uma falha, o restante cancelado.
        catch (Exception ex) when (entries.Count > 0)
#pragma warning restore CA1031
        {
            return [.. entries.Select((item, index) =>
            {
                var outcome = index == 0 && ex is not OperationCanceledException ? CommandOutcome.Failed : CommandOutcome.Cancelled;
                item.Slot.Desired = item.Slot.Applied;
                item.Slot.Message = ex.Message;
                item.Slot.State.TryMove(FeatureState.Error, ex.Message);
                var message = index == 0 ? $"Failed: {ex.Message}" : "Batch interrupted; this setting was not confirmed.";
                _journal.Complete(item.Entry, outcome, null, message, _time.GetElapsedTime(started));
                return Finish(item.Slot, new CommandResult(item.Entry.OperationId, item.Request.Key, outcome, item.Entry.Before, null, message));
            })];
        }
        finally { foreach (var slot in locked) { slot.Gate.Release(); } }
    }

    /// <summary>
    /// Aplica vários pedidos em ordem. Com <paramref name="rollbackOnFailure"/>, a primeira falha desfaz os já
    /// aplicados, do último para o primeiro (perfis e migração usam isto).
    /// </summary>
    public async Task<IReadOnlyList<CommandResult>> ApplyAllAsync(IReadOnlyList<ControlRequest> requests, bool rollbackOnFailure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var results = new List<CommandResult>();
        var done = new List<(ControlRequest Request, CommandResult Result)>();
        foreach (var request in requests)
        {
            var result = await ExecuteCoreAsync(request, UndoMode.Record, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            if (result.Succeeded)
            {
                done.Add((request, result));
                continue;
            }

            if (result.Outcome == CommandOutcome.Superseded || !rollbackOnFailure)
            {
                continue;
            }

            for (var i = done.Count - 1; i >= 0; i--)
            {
                var (previous, applied) = done[i];
                if (applied.Before is null)
                {
                    continue;
                }

                var reverted = await ExecuteCoreAsync(previous with { Value = applied.Before, Reason = "Batch rollback" }, UndoMode.Skip, cancellationToken).ConfigureAwait(false);
                var index = results.IndexOf(applied);
                results[index] = applied with
                {
                    Outcome = reverted.Succeeded ? CommandOutcome.RolledBack : CommandOutcome.Failed,
                    Message = reverted.Succeeded ? "Reverted because another change in the batch failed." : $"Could not revert: {reverted.Message}",
                };
            }

            break;
        }

        return results;
    }

    public async Task<CommandResult?> UndoAsync(CancellationToken cancellationToken = default)
    {
        UndoItem? item;
        lock (_gate)
        {
            item = _undo.Last?.Value;
            if (item is not null)
            {
                _undo.RemoveLast();
            }
        }

        if (item is null)
        {
            return null;
        }

        var result = await ExecuteCoreAsync(new ControlRequest(item.Key, item.Before, item.Writer, item.Priority, "Desfazer"), UndoMode.Skip, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (result.Succeeded)
            {
                _redo.Push(item);
            }
            else
            {
                _undo.AddLast(item);
            }
        }

        return result;
    }

    public async Task<CommandResult?> RedoAsync(CancellationToken cancellationToken = default)
    {
        UndoItem? item;
        lock (_gate)
        {
            _redo.TryPop(out item);
        }

        if (item is null)
        {
            return null;
        }

        var result = await ExecuteCoreAsync(new ControlRequest(item.Key, item.After, item.Writer, item.Priority, "Refazer"), UndoMode.Skip, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (result.Succeeded)
            {
                PushUndo(item);
            }
            else
            {
                _redo.Push(item);
            }
        }

        return result;
    }

    /// <summary>Volta ao padrão de fábrica do controle, quando o adapter informa um.</summary>
    public Task<CommandResult> ResetAsync(ControlKey key, string writer, CancellationToken cancellationToken = default)
    {
        var slot = Find(key);
        if (slot?.Adapter.Spec.Default is not { } value)
        {
            return Task.FromResult(new CommandResult(NewId(), key, CommandOutcome.Rejected, null, null, "This control has no known default."));
        }

        return ExecuteCoreAsync(new ControlRequest(key, value, writer, WriterPriority.TemporaryUserOverride, "Restore default"), UndoMode.Record, cancellationToken);
    }

    /// <summary>Descarta o pedido ainda na fila e volta a mostrar o valor aplicado (A28 "Cancelar").</summary>
    public void Cancel(ControlKey key)
    {
        var slot = Find(key);
        if (slot is null)
        {
            return;
        }

        Interlocked.Increment(ref slot.Version);
        slot.Desired = slot.Applied;
        Raise(slot);
    }

    /// <summary>Confirma uma mudança de alto risco dentro da janela (ponto seguro → aplicar → verificar → confirmar).</summary>
    public CommandResult Confirm(ControlKey key)
    {
        var slot = Find(key);
        PendingConfirmation? pending = null;
        if (slot is not null)
        {
            lock (_gate)
            {
                pending = slot.Confirmation;
                slot.Confirmation = null;
            }
        }

        if (slot is null || pending is null)
        {
            return new CommandResult(NewId(), key, CommandOutcome.Rejected, null, null, "No change is awaiting confirmation.");
        }

        slot.State.TryMove(FeatureState.Applied, "Confirmed by the user");
        var entry = _journal.Complete(pending.Entry, CommandOutcome.Verified, slot.Applied, "Confirmed by the user.", pending.Entry.Duration);
        var result = new CommandResult(entry.OperationId, key, CommandOutcome.Verified, pending.Entry.Before, slot.Applied, "Change confirmed.");
        Raise(slot);
        Executed?.Invoke(this, result);
        return result;
    }

    /// <summary>Reverte o que passou da janela de confirmação sem resposta. O host chama periodicamente.</summary>
    public async Task<int> RevertExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        List<Slot> expired;
        lock (_gate)
        {
            expired = [.. _slots.Values.Where(s => s.Confirmation is { } c && c.Deadline <= now)];
        }

        foreach (var slot in expired)
        {
            await RevertPendingAsync(slot, "Reverted: the change was not confirmed in time.", cancellationToken).ConfigureAwait(false);
        }

        return expired.Count;
    }

    /// <summary>Recusa uma mudança de alto risco agora ("Reverter" no aviso de confirmação).</summary>
    public async Task<CommandResult> RejectAsync(ControlKey key, CancellationToken cancellationToken = default)
    {
        var slot = Find(key);
        if (slot?.Confirmation is null)
        {
            return new CommandResult(NewId(), key, CommandOutcome.Rejected, null, null, "No change is awaiting confirmation.");
        }

        return await RevertPendingAsync(slot, "Reverted at the user's request.", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Solta a posse de um escritor (ex.: fim do ajuste manual, o perfil volta a mandar).</summary>
    public bool Release(ResourceId resource, string writer) => _arbiter.ReleaseWriter(resource, writer);

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var slot in _slots.Values)
            {
                slot.Gate.Dispose();
            }

            _slots.Clear();
        }
    }

    private async Task<CommandResult> ExecuteCoreAsync(ControlRequest request, UndoMode undo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var operationId = NewId();
        var slot = Find(request.Key);
        if (slot is null)
        {
            return Finish(null, new CommandResult(operationId, request.Key, CommandOutcome.Rejected, null, null, "Unknown control."));
        }

        var spec = slot.Adapter.Spec;
        var invalid = Validate(spec, request, out var value);
        if ((invalid ?? Gate(slot, request)) is { } refusal)
        {
            var outcome = invalid is null ? CommandOutcome.Blocked : CommandOutcome.Rejected;
            var entry = _journal.Begin(operationId, request.Key, spec.Label, request.Writer, request.Priority, spec.Risk, slot.Applied, request.Value, false, _time.GetUtcNow(), request.Reason);
            _journal.Complete(entry, outcome, slot.Applied, refusal, TimeSpan.Zero);
            return Finish(slot, new CommandResult(operationId, request.Key, outcome, slot.Applied, slot.Applied, refusal));
        }

        // Retorno imediato (A27): o valor pedido aparece como pendente antes de qualquer E/S.
        var version = Interlocked.Increment(ref slot.Version);
        slot.Desired = value;
        Raise(slot);

        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (version != Volatile.Read(ref slot.Version))
            {
                return Finish(null, new CommandResult(operationId, request.Key, CommandOutcome.Superseded, null, null, "Superseded by a newer value."));
            }

            if (slot.Confirmation is not null)
            {
                slot.Desired = slot.Applied;
                return Finish(slot, new CommandResult(operationId, request.Key, CommandOutcome.Rejected, slot.Applied, slot.Applied, "Confirm or revert the previous change first."));
            }

            var acquired = _arbiter.TryAcquire(request.Key.Resource, request.Writer, request.Priority);
            if (!acquired.Succeeded || acquired.Lease is not { } lease)
            {
                slot.Desired = slot.Applied;
                var message = acquired.Outcome == AcquireOutcome.RejectedExternalOwner
                    ? $"Blocked: {acquired.CurrentOwner} controls this resource. Close it before applying settings."
                    : $"Blocked: {acquired.CurrentOwner} currently has priority over this resource.";
                var entry = _journal.Begin(operationId, request.Key, spec.Label, request.Writer, request.Priority, spec.Risk, slot.Applied, value, false, _time.GetUtcNow(), request.Reason);
                _journal.Complete(entry, CommandOutcome.Blocked, slot.Applied, message, TimeSpan.Zero);
                return Finish(slot, new CommandResult(operationId, request.Key, CommandOutcome.Blocked, slot.Applied, slot.Applied, message));
            }

            return await WriteAsync(slot, request, value, lease, operationId, undo, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    private async Task<CommandResult> WriteAsync(Slot slot, ControlRequest request, ControlValue value, Lease lease, string operationId, UndoMode undo, CancellationToken cancellationToken)
    {
        var adapter = slot.Adapter;
        var spec = adapter.Spec;
        var started = _time.GetTimestamp();

        // Ponto seguro: lê antes de escrever (ADR-0005 regra 1).
        ControlValue? before = slot.Applied;
        if (spec.Readable)
        {
            try
            {
                before = await ReadAsync(adapter, cancellationToken).ConfigureAwait(false) ?? before;
                slot.Observed = before;
            }
#pragma warning disable CA1031 // Leitura prévia opcional: sem ela, o "antes" é o último valor aplicado.
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                slot.Message = $"Initial read failed: {ex.Message}";
            }
        }

        if (before is not null && before.Matches(value, spec.Tolerance) && (slot.Applied is not null || spec.PersistsOnDevice) && spec.Risk != RiskLevel.High)
        {
            // Já está no valor pedido: não escreve (poupa flash/EEPROM, ADR-0005 regra 3).
            slot.Desired = value;
            slot.Applied = value;
            return Finish(slot, new CommandResult(operationId, request.Key, CommandOutcome.Verified, before, before, "Unchanged: the device already used this value."));
        }

        var entry = _journal.Begin(operationId, request.Key, spec.Label, request.Writer, request.Priority, spec.Risk, before, value, spec.Reversible && before is not null, _time.GetUtcNow(), request.Reason);
        slot.State.TryMove(FeatureState.Applying, request.Reason ?? "Applying");
        Raise(slot);

        try
        {
            using var timeout = new CancellationTokenSource(spec.Timeout, _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                await adapter.WriteAsync(value, lease, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"The device did not respond within {spec.Timeout.TotalSeconds:0.#} s.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            slot.State.TryMove(FeatureState.Error, "Cancelled");
            slot.Desired = slot.Applied;
            _journal.Complete(entry, CommandOutcome.Cancelled, null, "Cancelled during the write.", _time.GetElapsedTime(started));
            return Finish(slot, new CommandResult(operationId, request.Key, CommandOutcome.Cancelled, before, null, "Cancelled."));
        }
#pragma warning disable CA1031 // Adapters são a fronteira com o hardware: qualquer falha vira estado "Error" com a mensagem nativa (A29).
        catch (Exception ex)
#pragma warning restore CA1031
        {
            slot.State.TryMove(FeatureState.Error, ex.Message);
            slot.Desired = slot.Applied;
            slot.Message = ex.Message;
            _journal.Complete(entry, CommandOutcome.Failed, null, ex.Message, _time.GetElapsedTime(started));
            return Finish(slot, new CommandResult(operationId, request.Key, CommandOutcome.Failed, before, null, $"Failed: {ex.Message}"));
        }

        // Verificação por leitura quando o protocolo permite.
        ControlValue? observed = null;
        if (spec.Readable)
        {
            slot.State.TryMove(FeatureState.Verification, "Verifying");
            try
            {
                observed = await ReadAsync(adapter, cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Leitura de verificação falhou: resultado fica "applied without confirmation".
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                slot.Message = $"Verification failed: {ex.Message}";
            }
        }

        var provider = CultureInfo.CurrentCulture;
        if (observed is not null && !observed.Matches(value, spec.Tolerance))
        {
            slot.Observed = observed;
            if (spec.Risk != RiskLevel.Safe && before is not null)
            {
                var restored = await TryRestoreAsync(adapter, before, lease, cancellationToken).ConfigureAwait(false);
                slot.State.TryMove(FeatureState.Recovering, "Restaurando valor anterior");
                slot.State.TryMove(restored ? FeatureState.Applied : FeatureState.Error, restored ? "Restaurado" : "Restore failed");
                slot.Desired = slot.Applied = restored ? before : slot.Applied;
                slot.Observed = restored ? before : observed; // a restauração confere por leitura quando o protocolo permite
                var text = restored
                    ? $"The device did not confirm {value.Display(spec, provider)} (read {observed.Display(spec, provider)}); the previous value was restored."
                    : $"The device returned {observed.Display(spec, provider)} and the previous value could not be restored.";
                _journal.Complete(entry, restored ? CommandOutcome.RolledBack : CommandOutcome.Failed, observed, text, _time.GetElapsedTime(started));
                return Finish(slot, new CommandResult(operationId, request.Key, restored ? CommandOutcome.RolledBack : CommandOutcome.Failed, before, observed, text));
            }

            slot.State.TryMove(FeatureState.Error, "Value differs from request");
            slot.Desired = slot.Applied = observed;
            var mismatch = $"The device returned {observed.Display(spec, provider)} instead of {value.Display(spec, provider)}.";
            _journal.Complete(entry, CommandOutcome.Failed, observed, mismatch, _time.GetElapsedTime(started));
            return Finish(slot, new CommandResult(operationId, request.Key, CommandOutcome.Failed, before, observed, mismatch));
        }

        slot.Applied = value;
        slot.Observed = observed ?? slot.Observed;
        slot.Message = null;
        if (undo == UndoMode.Record && spec.Reversible && before is not null && !before.Matches(value, spec.Tolerance))
        {
            lock (_gate)
            {
                PushUndo(new UndoItem(request.Key, before, value, $"{spec.Label}: {value.Display(spec, provider)}", request.Writer, request.Priority));
                _redo.Clear();
            }
        }

        if (spec.Risk == RiskLevel.High && before is not null)
        {
            var deadline = _time.GetUtcNow() + spec.ConfirmWindow;
            var waiting = _journal.Complete(entry, CommandOutcome.AwaitingConfirmation, observed ?? value, $"Applied; confirm by {deadline.ToLocalTime():HH:mm:ss} or it will be reverted.", _time.GetElapsedTime(started));
            slot.Confirmation = new PendingConfirmation(waiting, before, lease, deadline);
            slot.State.TryMove(FeatureState.Verification, "Awaiting confirmation");
            return Finish(slot, new CommandResult(operationId, request.Key, CommandOutcome.AwaitingConfirmation, before, observed ?? value,
                $"{spec.Label} applied. Confirm within {spec.ConfirmWindow.TotalSeconds:0} s or the previous value will be restored."));
        }

        slot.State.TryMove(FeatureState.Applied, spec.StoredOnly ? "Saved only in OpenGG" : "Applied");
        var outcomeKind = observed is null ? CommandOutcome.Applied : CommandOutcome.Verified;
        var done = spec.StoredOnly ? $"{spec.Label}: {value.Display(spec, provider)}. Saved only in OpenGG; device writes are unavailable."
            : outcomeKind == CommandOutcome.Verified
            ? $"{spec.Label}: {value.Display(spec, provider)} (confirmed by the device)."
            : $"{spec.Label}: {value.Display(spec, provider)} (the device does not support readback).";
        _journal.Complete(entry, outcomeKind, observed ?? value, done, _time.GetElapsedTime(started));
        return Finish(slot, new CommandResult(operationId, request.Key, outcomeKind, before, observed ?? value, done));
    }

    private async Task<CommandResult> RevertPendingAsync(Slot slot, string reason, CancellationToken cancellationToken)
    {
        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PendingConfirmation? pending;
            lock (_gate)
            {
                pending = slot.Confirmation;
                slot.Confirmation = null;
            }

            if (pending is null)
            {
                return new CommandResult(NewId(), slot.Adapter.Key, CommandOutcome.Rejected, null, null, "Nothing to revert (already confirmed or reverted).");
            }

            slot.State.TryMove(FeatureState.Recovering, reason);
            var restored = await TryRestoreAsync(slot.Adapter, pending.Before, pending.Lease, cancellationToken).ConfigureAwait(false);
            slot.State.TryMove(restored ? FeatureState.Applied : FeatureState.Error, reason);
            if (restored)
            {
                slot.Applied = slot.Desired = slot.Observed = pending.Before;
                lock (_gate)
                {
                    if (_undo.Last?.Value.Key == slot.Adapter.Key)
                    {
                        _undo.RemoveLast();
                    }
                }
            }

            var outcome = restored ? CommandOutcome.RolledBack : CommandOutcome.Failed;
            var text = restored ? reason : $"{reason} The previous value could not be restored.";
            _journal.Complete(pending.Entry, outcome, restored ? pending.Before : slot.Applied, text, pending.Entry.Duration);
            return Finish(slot, new CommandResult(pending.Entry.OperationId, slot.Adapter.Key, outcome, pending.Entry.Requested, pending.Before, text));
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    private async Task<bool> TryRestoreAsync(IControlAdapter adapter, ControlValue before, Lease lease, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = new CancellationTokenSource(adapter.Spec.Timeout, _time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            await adapter.WriteAsync(before, lease, linked.Token).ConfigureAwait(false);
            if (!adapter.Spec.Readable)
            {
                return true;
            }

            var check = await ReadAsync(adapter, cancellationToken).ConfigureAwait(false);
            return check is null || check.Matches(before, adapter.Spec.Tolerance);
        }
#pragma warning disable CA1031 // Restauração é melhor esforço; o resultado vira estado "Error" com a mensagem.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    private async Task<ControlValue?> ReadAsync(IControlAdapter adapter, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(adapter.Spec.Timeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        return await adapter.ReadAsync(linked.Token).ConfigureAwait(false);
    }

    /// <summary>Disponibilidade e liberação de escrita (ADR-0005 regra 1 e 6).</summary>
    private static string? Gate(Slot slot, ControlRequest request)
    {
        if (!slot.Adapter.IsAvailable || slot.State.State is FeatureState.Disconnected or FeatureState.Unavailable)
        {
            return "Blocked: the device is disconnected.";
        }

        if (!slot.Adapter.WriteEnabled)
        {
            return $"Blocked: {slot.Adapter.WriteDisabledReason ?? "writes disabled for this device."}";
        }

        return null;
    }

    private static string? Validate(ControlSpec spec, ControlRequest request, out ControlValue normalized) =>
        ControlValidation.Validate(spec, request.Value, request.Priority == WriterPriority.Safety, out normalized);

    private CommandResult Finish(Slot? slot, CommandResult result)
    {
        if (slot is not null)
        {
            Raise(slot);
        }

        Executed?.Invoke(this, result);
        return result;
    }

    private void PushUndo(UndoItem item)
    {
        _undo.AddLast(item);
        while (_undo.Count > UndoCapacity)
        {
            _undo.RemoveFirst();
        }
    }

    private Slot? Find(ControlKey key)
    {
        lock (_gate)
        {
            return _slots.GetValueOrDefault(key);
        }
    }

    private void Raise(Slot slot)
    {
        ControlStatus status;
        lock (_gate)
        {
            status = ToStatus(slot);
        }

        StatusChanged?.Invoke(this, status);
    }

    private ControlStatus ToStatus(Slot slot)
    {
        var key = slot.Adapter.Key;
        var last = _journal.LastWrite(key.Resource);
        var message = slot.Adapter.IsAvailable ? slot.Message : "Device disconnected.";
        var readOnly = slot.Adapter.IsAvailable && !slot.Adapter.WriteEnabled ? slot.Adapter.WriteDisabledReason ?? "writes disabled." : null;
        message ??= readOnly;

        return new ControlStatus(key, slot.Adapter.Spec, slot.State.State, slot.Desired, slot.Applied, slot.Observed, slot.Persistent,
            message, _arbiter.OwnerOf(key.Resource), last?.Writer, last?.Time, slot.Confirmation?.Deadline)
        {
            ReadOnlyReason = readOnly,
            Available = slot.Adapter.IsAvailable,
        };
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private enum UndoMode
    {
        Record,
        Skip,
    }

    private sealed record UndoItem(ControlKey Key, ControlValue Before, ControlValue After, string Label, string Writer, WriterPriority Priority);

    private sealed record PendingConfirmation(JournalEntry Entry, ControlValue Before, Lease Lease, DateTimeOffset Deadline);

    private sealed class Slot(IControlAdapter adapter, TimeProvider time)
    {
        public long Version;

        public IControlAdapter Adapter { get; set; } = adapter;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public StateMachine<FeatureState> State { get; } = new(FeatureState.Discovered, StateModels.Feature, time);

        public ControlValue? Desired { get; set; }

        public ControlValue? Applied { get; set; }

        public ControlValue? Observed { get; set; }

        public ControlValue? Persistent { get; set; }

        public string? Message { get; set; }

        public PendingConfirmation? Confirmation { get; set; }
    }
}
