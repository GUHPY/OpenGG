using System.Diagnostics;
using System.Runtime.InteropServices;
using OneRGB.Application.Lighting.Spatial;
using OneRGB.Hardware.Audio.Interop;

namespace OneRGB.Hardware.Audio;

/// <summary>
/// Loopback da saída padrão (WASAPI compartilhado, AUDCLNT_STREAMFLAGS_LOOPBACK) para os efeitos de áudio do canvas
/// (SRGB-03): o que está tocando vira nível e bandas no <see cref="EffectContext"/>. Com <see cref="Voice"/> ("Reagir à voz
/// do microfone"), lê o microfone padrão em modo compartilhado no lugar da saída: o Windows mostra o microfone em uso, nada
/// é gravado e a cadeia de voz não muda. Liga só enquanto um efeito ativo pede áudio; troca de dispositivo padrão reabre sozinho.
/// </summary>
public sealed class LoopbackCapture(EffectContext context) : IDisposable
{
    public const int SampleRate = 48000;
    private const uint AllContexts = 0x17;
    private const long Buffer = 1_000_000; // 100 ms
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan DefaultCheck = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();
    private Thread? _thread;
    private volatile bool _stop;
    private volatile bool _voice;

    public bool Running { get; private set; }

    /// <summary>A voz do microfone padrão em vez do som que sai (NGE-08); trocar reabre a captura no próximo pacote.</summary>
    public bool Voice
    {
        get => _voice;
        set => _voice = value;
    }

    /// <summary>Por que a captura não está entregando áudio (sem saída, dispositivo removido).</summary>
    public string? Error { get; private set; }

    public void Start()
    {
        lock (_gate)
        {
            if (_thread is not null)
            {
                return;
            }

            _stop = false;
            _thread = new Thread(Run) { IsBackground = true, Name = "OneRGB loopback" };
            _thread.Start();
            Running = true;
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_gate)
        {
            thread = _thread;
            _thread = null;
        }

        if (thread is null)
        {
            return;
        }

        _stop = true;
        if (thread != Thread.CurrentThread)
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }

        Running = false;
        context.SetBands([]);
    }

    public void Dispose() => Stop();

    private void Run()
    {
        var spectrum = new AudioSpectrum(SampleRate);
        while (!_stop)
        {
            try
            {
                Error = null;
                Capture(spectrum);
            }
            catch (Exception ex) when (ex is InvalidOperationException or COMException or InvalidCastException)
            {
                Error = ex.Message;
                context.SetBands([]);
                for (var i = 0; i < 20 && !_stop; i++)
                {
                    Thread.Sleep(100); // tenta de novo em 2 s (saída trocada, driver reiniciando)
                }
            }
        }
    }

    private void Capture(AudioSpectrum spectrum)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        IMMDevice? device = null;
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        var voice = _voice;
        var flow = voice ? EDataFlow.Capture : EDataFlow.Render;
        try
        {
            Hr(enumerator.GetDefaultAudioEndpoint(flow, ERole.Multimedia, out device), voice ? "find the default microphone" : "find the default output");
            Hr(device.GetId(out var id), "read the default device");
            var iid = typeof(IAudioClient).GUID;
            Hr(device.Activate(ref iid, AllContexts, 0, out var instance), voice ? "abrir o microfone" : "open the output");
            client = (IAudioClient)instance;
            var format = WaveFormat.Float(SampleRate, 1);
            Hr(client.Initialize(0, (voice ? 0u : AudioClientFlags.Loopback) | AudioClientFlags.AutoConvertPcm | AudioClientFlags.SrcDefaultQuality, Buffer, 0, ref format, 0),
                voice ? "abrir o microfone" : "abrir o loopback");
            var captureIid = typeof(IAudioCaptureClient).GUID;
            Hr(client.GetService(ref captureIid, out var service), "abrir o fluxo");
            capture = (IAudioCaptureClient)service;
            Hr(client.Start(), "iniciar o loopback");

            var block = new float[SampleRate / 10];
            var clock = Stopwatch.StartNew();
            var last = TimeSpan.Zero;
            var lastDefaultCheck = TimeSpan.Zero;
            long delivered = 0;
            while (!_stop && _voice == voice)
            {
                Thread.Sleep(Tick);
                while (true)
                {
                    Hr(capture.GetNextPacketSize(out var packet), "ler o loopback");
                    if (packet == 0)
                    {
                        break;
                    }

                    Hr(capture.GetBuffer(out var data, out var count, out var flags, out _, out _), "ler o loopback");
                    var n = (int)Math.Min(count, (uint)block.Length);
                    if ((flags & AudioClientFlags.Silent) != 0)
                    {
                        block.AsSpan(0, n).Clear();
                    }
                    else
                    {
                        Marshal.Copy(data, block, 0, n);
                    }

                    Hr(capture.ReleaseBuffer(count), "ler o loopback");
                    spectrum.Push(block.AsSpan(0, n));
                    delivered += n;
                }

                var now = clock.Elapsed;
                // Nada tocando: o loopback não entrega pacote. Completa com silêncio para a luz cair em vez de congelar.
                var expected = (long)(now.TotalSeconds * SampleRate);
                if (expected - delivered > SampleRate / 20)
                {
                    var gap = (int)Math.Min(expected - delivered, AudioSpectrum.FftSize);
                    spectrum.PushSilence(gap);
                    delivered = expected;
                }

                spectrum.Analyze((now - last).TotalSeconds);
                last = now;
                context.SetBands(spectrum.Bands);
                context.AudioLevel = spectrum.Level;

                if (now - lastDefaultCheck >= DefaultCheck)
                {
                    lastDefaultCheck = now;
                    if (enumerator.GetDefaultAudioEndpoint(flow, ERole.Multimedia, out var current) == 0)
                    {
                        if (current.GetId(out var currentId) == 0 && !string.Equals(currentId, id, StringComparison.Ordinal))
                        {
                            return; // dispositivo padrão trocou: reabre no novo
                        }
                    }
                }
            }
        }
        finally
        {
            _ = client?.Stop();
            // Só o fluxo é nosso. O enumerador (e os dispositivos que ele devolve) pode ser o mesmo objeto COM que o
            // AudioService usa: soltar o RCW à força quebraria a página Áudio.
            foreach (var o in new object?[] { capture, client })
            {
                if (o is not null)
                {
                    Marshal.FinalReleaseComObject(o);
                }
            }
        }
    }

    private static void Hr(int hr, string what)
    {
        if (hr != 0)
        {
            throw new InvalidOperationException(hr switch
            {
                unchecked((int)0x88890004) => "The audio device disconnected.",
                unchecked((int)0x80070490) => "No active audio device.",
                unchecked((int)0x80070005) => "O Windows negou o acesso ao microfone (Privacidade › Microfone).",
                _ => $"Windows rejected {what} (0x{hr:X8}).",
            });
        }
    }
}
