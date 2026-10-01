using OneRGB.Windows;

namespace OneRGB.Hardware.Native;

/// <summary>
/// Pedido e resposta por HID nas coleções de um dispositivo (HID++ curto e longo, base do A50). Cada relatório lido
/// é conferido contra o pedido pendente; avisos espontâneos e respostas a outros apps que usam a mesma coleção (o
/// G HUB, por exemplo) são ignorados. Abre por operação: um dispositivo que saiu vira erro na próxima, sem estado para
/// reabrir. Não é criado pela UI: só as integrações abrem, depois de conferir VID, PID e página.
/// </summary>
internal sealed class HidChannel : IAsyncDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private readonly (HidInterfaceInfo Info, FileStream Stream)[] _collections;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task[] _pumps;
    private volatile Pending? _pending;

    private HidChannel((HidInterfaceInfo Info, FileStream Stream)[] collections)
    {
        _collections = collections;
        _pumps = [.. collections.Select(c => PumpAsync(c.Stream, c.Info.InputReportLength))];
    }

    public static HidChannel Open(IEnumerable<HidInterfaceInfo> collections)
    {
        var opened = new List<(HidInterfaceInfo, FileStream)>();
        try
        {
            foreach (var info in collections)
            {
                var handle = File.OpenHandle(info.DevicePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, FileOptions.Asynchronous);
                opened.Add((info, new FileStream(handle, FileAccess.ReadWrite, 0, isAsync: true)));
            }
        }
        catch
        {
            foreach (var (_, stream) in opened)
            {
                stream.Dispose();
            }

            throw;
        }

        return new HidChannel([.. opened]);
    }

    /// <summary>Abre, roda e fecha, um de cada vez por <paramref name="gate"/> (o dispositivo atende um pedido por vez).</summary>
    public static async Task<T> UseAsync<T>(SemaphoreSlim gate, IEnumerable<HidInterfaceInfo> collections, Func<HidChannel, Task<T>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(work);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var channel = Open(collections);
            await using (channel.ConfigureAwait(false))
            {
                return await work(channel).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Envia e espera a resposta que <paramref name="answers"/> reconhece.</summary>
    public async Task<byte[]> RequestAsync(byte[] report, Func<byte[], bool> answers, CancellationToken cancellationToken,
        Func<Task>? send = null, TimeSpan? timeout = null)
    {
        var wait = timeout ?? Timeout;
        var pending = new Pending(answers);
        _pending = pending;
        try
        {
            await (send?.Invoke() ?? SendAsync(report, cancellationToken)).ConfigureAwait(false);
            return await pending.Reply.Task.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"The device did not respond within {wait.TotalSeconds:0} s.");
        }
        finally
        {
            _pending = null;
        }
    }

    /// <summary>Envia pela coleção com o menor relatório de saída que cabe o pacote, completando com zeros.</summary>
    public async Task SendAsync(byte[] report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        var target = _collections.Where(c => c.Info.OutputReportLength >= report.Length).OrderBy(c => c.Info.OutputReportLength).FirstOrDefault();
        if (target.Stream is null)
        {
            throw new InvalidOperationException($"No collection accepts a {report.Length}-byte report. Nothing was sent.");
        }

        var buffer = new byte[target.Info.OutputReportLength];
        report.CopyTo(buffer, 0);
        await target.Stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(_pumps).ConfigureAwait(false);
        foreach (var (_, stream) in _collections)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }

        _stop.Dispose();
    }

    private async Task PumpAsync(FileStream stream, int length)
    {
        var buffer = new byte[length];
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer, _stop.Token).ConfigureAwait(false);
                if (read > 0 && _pending is { } pending && pending.Answers(buffer[..read]))
                {
                    pending.Reply.TrySetResult(buffer[..read]);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException ex)
        {
            // Dispositivo saiu no meio: o pedido pendente falha já, sem esperar o tempo limite.
            _pending?.Reply.TrySetException(ex);
        }
    }

    private sealed class Pending(Func<byte[], bool> answers)
    {
        public Func<byte[], bool> Answers { get; } = answers;

        public TaskCompletionSource<byte[]> Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
