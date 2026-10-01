namespace OneRGB.Application.Devices.Keyboard;

/// <summary>Uma saída lógica mudou (tecla A ou a ação funda de uma tecla 2-em-1).</summary>
public readonly record struct KeyOutputChange(string Output, bool Down);

/// <summary>
/// Reproduz o que o firmware faz com a profundidade de cada tecla (mm, 0 = solta): ponto de atuação, Rapid
/// Trigger, Protection Mode, 2-em-1 e Rapid Tap. Serve para a prévia ao vivo (A40.2) e para testar as regras —
/// o teclado faz isso sozinho, o OneRGB só mostra o que vai acontecer.
/// <para>Saídas: o código da tecla (ação A) e <c>código#deep</c> (ação B da 2-em-1).</para>
/// </summary>
public sealed class AnalogKeySimulator
{
    /// <summary>Profundidade considerada "tecla toda solta" para o Rapid Trigger contínuo.</summary>
    public const double FullyReleasedMm = 0.05;

    public const string DeepSuffix = "#deep";

    private readonly KeyboardConfig _config;
    private readonly Dictionary<string, Track> _tracks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _protected;
    private readonly HashSet<string> _affected;
    private HashSet<string> _active = new(StringComparer.Ordinal);
    private long _sequence;

    public AnalogKeySimulator(KeyboardConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _protected = new HashSet<string>(config.Protection.Enabled ? config.Protection.Keys : [], StringComparer.Ordinal);
        _affected = new HashSet<string>(config.Protection.Enabled ? KeyboardRules.AffectedKeys(config) : [], StringComparer.Ordinal);
    }

    /// <summary>O Protection Mode está endurecendo as vizinhas agora.</summary>
    public bool ProtectionActive { get; private set; }

    public IReadOnlySet<string> Active => _active;

    /// <summary>Ponto de atuação efetivo de uma tecla agora (com o Protection Mode).</summary>
    public double EffectiveActuation(string code)
    {
        var device = _config.Device;
        var baseline = _config.Actuation.GetValueOrDefault(code, device.DefaultActuation);
        return ProtectionActive && _affected.Contains(code)
            ? Math.Min(device.Actuation.Maximum, baseline + _config.Protection.ReductionMm)
            : baseline;
    }

    /// <summary>
    /// Avança um instante. <paramref name="depths"/> traz a profundidade das teclas que mudaram ou estão
    /// pressionadas; teclas ausentes contam como soltas. Devolve o que mudou nas saídas.
    /// </summary>
    public IReadOnlyList<KeyOutputChange> Step(IReadOnlyDictionary<string, double> depths)
    {
        ArgumentNullException.ThrowIfNull(depths);
        double DepthOf(string code) => depths.TryGetValue(code, out var d) && double.IsFinite(d) ? Math.Max(0, d) : 0;

        // Protection Mode é decidido antes de qualquer outra tecla mudar de estado (A5.7).
        foreach (var code in _protected)
        {
            Advance(code, DepthOf(code), _config.Actuation.GetValueOrDefault(code, _config.Device.DefaultActuation));
        }

        ProtectionActive = _config.Protection.Enabled
            && (_config.Protection.Activation == ProtectionActivation.Always || _protected.Any(c => _tracks.TryGetValue(c, out var t) && t.Pressed));

        foreach (var code in depths.Keys.Concat(_tracks.Keys).Distinct(StringComparer.Ordinal).ToList())
        {
            if (!_protected.Contains(code))
            {
                Advance(code, DepthOf(code), EffectiveActuation(code));
            }
        }

        var next = Resolve();
        var changes = new List<KeyOutputChange>();
        changes.AddRange(_active.Where(o => !next.Contains(o)).Order(StringComparer.Ordinal).Select(o => new KeyOutputChange(o, false)));
        changes.AddRange(next.Where(o => !_active.Contains(o)).Order(StringComparer.Ordinal).Select(o => new KeyOutputChange(o, true)));
        _active = next;
        return changes;
    }

    private void Advance(string code, double depth, double actuation)
    {
        if (!_tracks.TryGetValue(code, out var track))
        {
            if (depth <= 0)
            {
                return;
            }

            track = new Track();
            _tracks[code] = track;
        }

        var device = _config.Device;
        var rt = _config.RapidTrigger.GetValueOrDefault(code);
        var rapid = rt is { Enabled: true } && device.Key(code)?.Has(KeyCapabilities.RapidTrigger) == true;
        var step = device.Actuation.Step;

        if (rapid)
        {
            var continuous = device.ContinuousRapidTrigger;
            if (track.Pressed)
            {
                track.Peak = Math.Max(track.Peak, depth);
                var belowZone = continuous ? depth <= FullyReleasedMm : depth < actuation;
                if (belowZone || depth <= track.Peak - rt!.ReleaseMm + 1e-9)
                {
                    track.Pressed = false;
                    track.Trough = depth;
                    track.Armed = !belowZone;
                }
            }
            else
            {
                track.Trough = Math.Min(track.Trough, depth);
                var inZone = continuous ? depth > FullyReleasedMm : depth >= actuation;
                if (!inZone)
                {
                    track.Armed = false;
                }

                // Armado, só a sensibilidade pressiona de novo (senão a tecla nunca soltaria abaixo do ponto).
                var first = !track.Armed && depth >= actuation - 1e-9;
                var again = track.Armed && inZone && depth >= track.Trough + rt!.PressMm - 1e-9;
                if (first || again)
                {
                    Press(track, depth);
                }
            }
        }
        else if (track.Pressed)
        {
            // Sem Rapid Trigger: solta um passo acima do ponto de atuação (histerese contra oscilação).
            if (depth <= Math.Max(0, actuation - step) + 1e-9)
            {
                track.Pressed = false;
            }
        }
        else if (depth >= actuation - 1e-9)
        {
            Press(track, depth);
        }

        // 2-em-1: a ação funda liga no segundo ponto e desliga um passo acima dele.
        if (_config.Dual.TryGetValue(code, out var dual) && track.Pressed)
        {
            if (!track.Deep && depth >= dual.DeepMm - 1e-9)
            {
                track.Deep = true;
            }
            else if (track.Deep && depth <= dual.DeepMm - step + 1e-9)
            {
                track.Deep = false;
            }
        }
        else
        {
            track.Deep = false;
        }

        track.Depth = depth;
        if (!track.Pressed && depth <= 0)
        {
            _tracks.Remove(code);
        }
    }

    private void Press(Track track, double depth)
    {
        track.Pressed = true;
        track.Peak = depth;
        track.Armed = true;
        track.Order = ++_sequence;
    }

    private HashSet<string> Resolve()
    {
        var outputs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (code, track) in _tracks)
        {
            if (!track.Pressed)
            {
                continue;
            }

            var dual = _config.Dual.GetValueOrDefault(code);
            if (!track.Deep || dual is null || dual.KeepFirst)
            {
                outputs.Add(code);
            }

            if (track.Deep)
            {
                outputs.Add(code + DeepSuffix);
            }
        }

        foreach (var pair in _config.RapidTapPairs.Where(p => p.Enabled))
        {
            if (!outputs.Contains(pair.First) || !outputs.Contains(pair.Second))
            {
                continue;
            }

            switch (pair.Mode)
            {
                case RapidTapMode.LastInput:
                    outputs.Remove(_tracks[pair.First].Order > _tracks[pair.Second].Order ? pair.Second : pair.First);
                    break;
                case RapidTapMode.FirstKey:
                    outputs.Remove(pair.Second);
                    break;
                case RapidTapMode.SecondKey:
                    outputs.Remove(pair.First);
                    break;
                default:
                    outputs.Remove(pair.First);
                    outputs.Remove(pair.Second);
                    break;
            }
        }

        return outputs;
    }

    private sealed class Track
    {
        public bool Pressed { get; set; }

        public bool Deep { get; set; }

        /// <summary>Rapid Trigger armado: pode pressionar de novo pela sensibilidade, sem voltar ao ponto de atuação.</summary>
        public bool Armed { get; set; }

        public double Depth { get; set; }

        public double Peak { get; set; }

        public double Trough { get; set; } = double.MaxValue;

        public long Order { get; set; }
    }
}
