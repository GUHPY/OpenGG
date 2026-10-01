using System.ComponentModel;
using OneRGB.Application;
using OneRGB.Application.Devices.Keyboard;
using OneRGB.Application.Lighting;
using OneRGB.Application.Lighting.Spatial;
using OneRGB.Hardware.Native;
using OneRGB.Windows;

namespace OneRGB.Hardware.Lighting;

/// <summary>
/// O canvas no Apex Pro TKL Gen 3 direto pelo HID (<see cref="ApexProtocol"/>), sem o GG e sem o GameSense.
/// Envio:
/// - um laço só fala com o teclado, sempre com o quadro mais novo;
/// - no máximo 30 quadros por segundo, e só quando o quadro muda: o receptor sem fio divide o rádio com as teclas;
/// - a cada 5 s repete o último quadro, para o teclado que acordou.
/// Soltar libera a iluminação temporária (0x62 no receptor), sem reset de teclado.
/// ADR-0005: nada é escrito sem o catálogo liberar.
/// </summary>
public sealed class ApexLighting : IFrameSink, IDisposable
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan Repeat = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(2);
    private readonly ControlArbiter _arbiter;
    private readonly Func<IReadOnlyList<HidInterfaceInfo>?> _interfaces;
    private readonly Func<bool> _writesAllowed;
    private readonly string _lockedReason;
    private readonly byte?[] _leds = [.. LedMaps.Keyboard.Leds.Select(l => ApexProtocol.LedId(l.Id))];
    private readonly Rgb[] _frame = new Rgb[LedMaps.Keyboard.Leds.Count];
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly ApexHidLink _link;
    private readonly Task _loop;
    private Lease? _lease;

    /// <param name="interfaces">Interfaces HID da última varredura.</param>
    /// <param name="writesAllowed">O catálogo libera a escrita no teclado (verified_hw).</param>
    /// <param name="lockedReason">Por que a escrita está travada, para a tela.</param>
    /// <param name="link">Canal HID compartilhado com a atuação, para os SetFeature não se cruzarem.</param>
    public ApexLighting(ControlArbiter arbiter, Func<IReadOnlyList<HidInterfaceInfo>?> interfaces, Func<bool> writesAllowed, string lockedReason, ApexHidLink link)
    {
        _arbiter = arbiter ?? throw new ArgumentNullException(nameof(arbiter));
        _interfaces = interfaces ?? throw new ArgumentNullException(nameof(interfaces));
        _writesAllowed = writesAllowed ?? throw new ArgumentNullException(nameof(writesAllowed));
        _lockedReason = lockedReason;
        _link = link ?? throw new ArgumentNullException(nameof(link));
        _loop = Task.Run(RunAsync);
    }

    public string DeviceId => LedMaps.KeyboardId;

    public string? LastError { get; private set; }

    public ResourceId Resource { get; } = new($"{LedMaps.KeyboardId}/rgb");

    public bool IsAvailable => Find(_interfaces() ?? []) is not null;

    public bool WriteEnabled => _writesAllowed();

    public string? WriteDisabledReason => WriteEnabled ? null : _lockedReason;

    /// <summary>A interface de controle (página 0xFFC0) na interface do modelo; nula sem teclado.</summary>
    public static HidInterfaceInfo? Find(IEnumerable<HidInterfaceInfo> interfaces) =>
        interfaces.FirstOrDefault(i => i.Error is null
            && i.Identity.VendorId == ApexProtocol.VendorId
            && i.Identity.ProductId == 0x1644
            && i.Identity.UsagePage == ApexProtocol.UsagePage
            && i.Identity.Usage == 1 && i.Identity.InterfaceNumber == 3
            && i.FeatureReportLength == 642 && i.InputReportLength == 65 && i.OutputReportLength == 65);

    public void Submit(Lease lease, ReadOnlySpan<Rgb> frame)
    {
        lock (_gate)
        {
            _lease = lease;
            var n = Math.Min(frame.Length, _frame.Length);
            frame[..n].CopyTo(_frame);
            _frame.AsSpan(n).Clear();
        }

        Wake();
    }

    public void Release()
    {
        lock (_gate)
        {
            _lease = null;
        }

        Wake();
    }

    /// <summary>Para o envio; antes, o teclado volta à iluminação do perfil.</summary>
    public void Dispose()
    {
        Release();
        _stop.Cancel();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // encerrando
        }

        _stop.Dispose();
        _wake.Dispose();
    }

    private void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // já acordado: o laço pega o quadro mais novo
        }
        catch (ObjectDisposedException)
        {
            // encerrando
        }
    }

    /// <summary>Um envio por vez; soltar vem do mesmo laço, depois do último quadro.</summary>
    private async Task RunAsync()
    {
        var token = _stop.Token;
        byte[]? sent = null;
        try
        {
            while (true)
            {
                var woke = await _wake.WaitAsync(Repeat, token).ConfigureAwait(false);
                try
                {
                    if (Packet() is not { } packet)
                    {
                        if (sent is not null && MayWrite())
                        {
                            _link.ClearTemporary();
                        }

                        sent = null;
                        continue;
                    }

                    if (woke && sent is not null && packet.AsSpan().SequenceEqual(sent))
                    {
                        continue;
                    }

                    await _link.SendFeatureAsync(packet, token).ConfigureAwait(false);
                    LastError = null;
                    sent = packet;
                    await Task.Delay(FrameInterval, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or TimeoutException)
                {
                    LastError = ex.Message;
                    _link.Drop();
                    sent = null;
                    await Task.Delay(RetryAfterFailure, token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // encerrando
        }

        if (sent is not null && MayWrite())
        {
            _link.ClearTemporary();
        }
    }

    /// <summary>Escrita liberada e nenhum outro app no controle.</summary>
    private bool MayWrite() => _writesAllowed() && _arbiter.ExternalOwnerOf(Resource) is null;

    /// <summary>O quadro para o teclado agora; nulo sem posse vigente ou sem <see cref="MayWrite"/>.</summary>
    private byte[]? Packet()
    {
        if (!MayWrite() || Find(_interfaces() ?? []) is not { } device || ApexProtocol.Model(device.Identity.ProductId) is not { } model)
        {
            return null;
        }

        lock (_gate)
        {
            return _lease is { } lease && _arbiter.IsCurrent(lease) ? ApexProtocol.Frame(model.Opcode, _leds, _frame) : null;
        }
    }
}
