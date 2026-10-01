namespace OneRGB.Application.Lighting.Spatial;

/// <summary>
/// Espectro para os efeitos de áudio (SRGB-03, SRGB-AUDIO-001): FFT de 1024 pontos com janela de Hann sobre as
/// últimas amostras do loopback, bandas em escala log de frequência (40 Hz a 16 kHz) e suavização com ataque rápido e
/// queda lenta, para a luz acompanhar a batida sem piscar. Tudo em buffers próprios: nenhuma alocação por quadro.
/// </summary>
public sealed class AudioSpectrum
{
    public const int FftSize = 1024;
    public const double MinimumHz = 40;
    public const double MaximumHz = 16000;

    /// <summary>Piso da escala: -60 dBFS é zero, 0 dBFS (senoide cheia) é um.</summary>
    public const double FloorDb = -60;

    private const double AttackSeconds = 0.02;
    private const double ReleaseSeconds = 0.25;

    private readonly float[] _ring = new float[FftSize];
    private readonly double[] _re = new double[FftSize];
    private readonly double[] _im = new double[FftSize];
    private readonly double[] _window = new double[FftSize];
    private readonly double[] _cos = new double[FftSize / 2];
    private readonly double[] _sin = new double[FftSize / 2];
    private readonly int[] _first;
    private readonly int[] _last;
    private readonly double[] _bands;
    private readonly double[] _raw;
    private int _write;
    private int _filled;
    private double _sumSquares;
    private int _sumCount;
    private double _level;

    public AudioSpectrum(int sampleRate, int bandCount = EffectContext.BandCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8000);
        ArgumentOutOfRangeException.ThrowIfLessThan(bandCount, 1);
        SampleRate = sampleRate;
        _bands = new double[bandCount];
        _raw = new double[bandCount];
        _first = new int[bandCount];
        _last = new int[bandCount];
        for (var i = 0; i < FftSize; i++)
        {
            _window[i] = 0.5 - (0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1)));
        }

        for (var i = 0; i < FftSize / 2; i++)
        {
            (_sin[i], _cos[i]) = Math.SinCos(-2 * Math.PI * i / FftSize);
        }

        // Bordas log das bandas; banda estreita demais para ter um bin fica com o bin mais perto do centro.
        var binHz = (double)sampleRate / FftSize;
        var top = Math.Min(MaximumHz, sampleRate / 2.0);
        for (var b = 0; b < bandCount; b++)
        {
            var low = MinimumHz * Math.Pow(top / MinimumHz, (double)b / bandCount);
            var high = MinimumHz * Math.Pow(top / MinimumHz, (double)(b + 1) / bandCount);
            var first = (int)Math.Ceiling(low / binHz);
            var last = (int)Math.Floor(high / binHz);
            if (last < first)
            {
                first = last = (int)Math.Round(Math.Sqrt(low * high) / binHz);
            }

            _first[b] = Math.Clamp(first, 1, (FftSize / 2) - 1);
            _last[b] = Math.Clamp(last, _first[b], (FftSize / 2) - 1);
        }
    }

    public int SampleRate { get; }

    /// <summary>Bandas suavizadas (0 a 1), dos graves aos agudos.</summary>
    public ReadOnlySpan<double> Bands => _bands;

    /// <summary>Nível RMS suavizado das amostras recebidas desde a última análise (0 a 1, na mesma escala em dB).</summary>
    public double Level => _level;

    /// <summary>Amostras mono novas (float, -1 a 1). Chamado pela thread de captura.</summary>
    public void Push(ReadOnlySpan<float> samples)
    {
        foreach (var s in samples)
        {
            var v = float.IsFinite(s) ? s : 0;
            _ring[_write] = v;
            _write = (_write + 1) % FftSize;
            _sumSquares += v * v;
            _sumCount++;
        }

        _filled = Math.Min(FftSize, _filled + samples.Length);
    }

    /// <summary>Quantos segundos de silêncio: o loopback não entrega pacote enquanto nada toca.</summary>
    public void PushSilence(int frames)
    {
        Span<float> zeros = stackalloc float[256];
        zeros.Clear();
        while (frames > 0)
        {
            var n = Math.Min(frames, zeros.Length);
            Push(zeros[..n]);
            frames -= n;
        }
    }

    /// <summary>Roda a FFT sobre a janela atual e aplica a suavização por <paramref name="elapsedSeconds"/>.</summary>
    public void Analyze(double elapsedSeconds)
    {
        var dt = double.IsFinite(elapsedSeconds) ? Math.Clamp(elapsedSeconds, 0, 1) : 0;
        for (var i = 0; i < FftSize; i++)
        {
            // O mais antigo primeiro; faltando amostras (começo), o começo da janela é silêncio.
            var sample = i < FftSize - _filled ? 0 : _ring[(_write + i) % FftSize];
            _re[i] = sample * _window[i];
            _im[i] = 0;
        }

        Transform(_re, _im);

        // Senoide de amplitude 1 com Hann: pico do bin ≈ N/4.
        const double fullScale = FftSize / 4.0;
        for (var b = 0; b < _raw.Length; b++)
        {
            var peak = 0.0;
            for (var k = _first[b]; k <= _last[b]; k++)
            {
                peak = Math.Max(peak, Math.Sqrt((_re[k] * _re[k]) + (_im[k] * _im[k])) / fullScale);
            }

            _raw[b] = ToScale(peak);
        }

        var rms = _sumCount == 0 ? 0 : Math.Sqrt(_sumSquares / _sumCount) * Math.Sqrt(2); // senoide cheia = 1
        (_sumSquares, _sumCount) = (0, 0);
        _level = Smooth(_level, ToScale(rms), dt);
        for (var b = 0; b < _bands.Length; b++)
        {
            _bands[b] = Smooth(_bands[b], _raw[b], dt);
        }
    }

    /// <summary>Amplitude linear para 0 a 1 em dB (-60 a 0 dBFS).</summary>
    public static double ToScale(double amplitude) =>
        amplitude <= 0 || !double.IsFinite(amplitude) ? 0 : Math.Clamp((20 * Math.Log10(amplitude) - FloorDb) / -FloorDb, 0, 1);

    private static double Smooth(double current, double target, double dt)
    {
        if (dt <= 0)
        {
            return target;
        }

        var tau = target > current ? AttackSeconds : ReleaseSeconds;
        return current + ((target - current) * (1 - Math.Exp(-dt / tau)));
    }

    /// <summary>FFT radix-2 iterativa, no lugar.</summary>
    private void Transform(double[] re, double[] im)
    {
        for (int i = 1, j = 0; i < FftSize; i++)
        {
            var bit = FftSize >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var size = 2; size <= FftSize; size <<= 1)
        {
            var half = size / 2;
            var step = FftSize / size;
            for (var start = 0; start < FftSize; start += size)
            {
                for (var k = 0; k < half; k++)
                {
                    var wr = _cos[k * step];
                    var wi = _sin[k * step];
                    var a = start + k;
                    var b = a + half;
                    var tr = (re[b] * wr) - (im[b] * wi);
                    var ti = (re[b] * wi) + (im[b] * wr);
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
            }
        }
    }
}
