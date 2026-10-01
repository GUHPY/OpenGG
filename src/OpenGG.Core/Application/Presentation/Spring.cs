namespace OneRGB.Application.Presentation;

/// <summary>Rigidez e amortecimento de uma mola; ζ = amortecimento / (2·√rigidez).</summary>
public readonly record struct SpringConfig(double Stiffness, double Damping)
{
    /// <summary>ζ ≈ 1: criticamente amortecida, sem ultrapassar o alvo (padrão do app, spec §75).</summary>
    public static SpringConfig Smooth { get; } = new(170, 26);

    /// <summary>Criticamente amortecida e mais rápida, para respostas diretas (número que muda, alça que assenta).</summary>
    public static SpringConfig Quick { get; } = new(420, 41);

    public double DampingRatio => Stiffness <= 0 ? 0 : Damping / (2 * Math.Sqrt(Stiffness));
}

/// <summary>
/// Mola amortecida sobre o progresso x: 0 → 1 (porte de morphicons/src/core/spring.ts, MIT):
/// ẍ = k·(1−x) − c·ẋ, integrada por Euler semi-implícito em subpassos de 1/240 s. Interrompível: <see cref="Restart"/>
/// zera x mantendo a velocidade (limitada a ±14), para uma animação nova continuar do embalo da anterior.
/// </summary>
public sealed class DampedSpring(SpringConfig config)
{
    private const double Substep = 1.0 / 240;
    private const double MaxRestartVelocity = 14;

    public DampedSpring()
        : this(SpringConfig.Smooth)
    {
    }

    public SpringConfig Config { get; } = config;

    public double Position { get; private set; } = 1;

    public double Velocity { get; private set; }

    public bool Settled => Math.Abs(1 - Position) < 0.001 && Math.Abs(Velocity) < 0.02;

    /// <summary>Começa (ou recomeça no meio do caminho) preservando a velocidade.</summary>
    public void Restart()
    {
        Position = 0;
        Velocity = Math.Clamp(Velocity, -MaxRestartVelocity, MaxRestartVelocity);
    }

    /// <summary>Avança <paramref name="seconds"/>; devolve verdadeiro quando assentou.</summary>
    public bool Step(double seconds)
    {
        if (seconds <= 0)
        {
            return Settled;
        }

        var steps = (int)Math.Clamp(Math.Ceiling(seconds / Substep), 1, 16);
        var h = seconds / steps;
        for (var i = 0; i < steps; i++)
        {
            var acceleration = (Config.Stiffness * (1 - Position)) - (Config.Damping * Velocity);
            Velocity += acceleration * h;
            Position += Velocity * h;
        }

        return Settled;
    }

    /// <summary>Tempo até assentar partindo do repouso em 0 (duração natural de uma animação com esta mola).</summary>
    public static TimeSpan SettleTime(SpringConfig config)
    {
        var spring = new DampedSpring(config) { Position = 0 };
        var elapsed = 0.0;
        while (!spring.Step(Substep) && elapsed < 10)
        {
            elapsed += Substep;
        }

        return TimeSpan.FromSeconds(elapsed + Substep);
    }

    /// <summary>
    /// Curva da mola amostrada do repouso até assentar, com <paramref name="samples"/> pontos igualmente espaçados no
    /// tempo: vira um easing de duração fixa (a WPF anima por tempo normalizado).
    /// </summary>
    public static double[] Curve(SpringConfig config, int samples = 121)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(samples, 2);
        var total = SettleTime(config).TotalSeconds;
        var dt = total / (samples - 1);
        var spring = new DampedSpring(config) { Position = 0 };
        var curve = new double[samples];
        for (var i = 1; i < samples; i++)
        {
            spring.Step(dt);
            curve[i] = spring.Position;
        }

        curve[^1] = 1;
        return curve;
    }

    /// <summary>Lê a curva no tempo normalizado t ∈ [0, 1], interpolando entre amostras.</summary>
    public static double Sample(IReadOnlyList<double> curve, double t)
    {
        ArgumentNullException.ThrowIfNull(curve);
        if (t <= 0)
        {
            return curve[0];
        }

        if (t >= 1)
        {
            return curve[^1];
        }

        var position = t * (curve.Count - 1);
        var index = (int)position;
        var fraction = position - index;
        return curve[index] + ((curve[index + 1] - curve[index]) * fraction);
    }
}
