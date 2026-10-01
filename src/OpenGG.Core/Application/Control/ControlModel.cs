using System.Globalization;
using System.Text.Json.Serialization;

namespace OneRGB.Application.Control;

/// <summary>Um controle de um recurso: <c>kbd-apex-pro-tkl-gen3/actuation</c> + <c>actuation.global</c>.</summary>
[JsonConverter(typeof(ControlKeyJsonConverter))]
public readonly record struct ControlKey(ResourceId Resource, string Setting)
{
    public static ControlKey Parse(string text) =>
        TryParse(text, out var key) ? key : throw new FormatException($"Invalid control key: '{text}'.");

    /// <summary>Lê <c>"recurso#ajuste"</c>. Recursos nunca têm '#'; o ajuste pode ter.</summary>
    public static bool TryParse(string? text, out ControlKey key)
    {
        key = default;
        var split = text?.IndexOf('#', StringComparison.Ordinal) ?? -1;
        if (text is null || split <= 0 || split == text.Length - 1)
        {
            return false;
        }

        key = new ControlKey(new ResourceId(text[..split]), text[(split + 1)..]);
        return true;
    }

    public override string ToString() => $"{Resource.Value}#{Setting}";
}

public enum ControlKind
{
    Number,
    Toggle,
    Choice,
    Color,
    Curve,
    Text,
}

/// <summary>
/// Fase na aplicação de um perfil (A31: ordem por dependência e segurança). Proteção entra antes de desempenho —
/// a curva de ventoinha sobe antes do limite de potência — e a reversão acontece na ordem inversa.
/// </summary>
public enum ApplyPhase
{
    /// <summary>Ventoinhas, bomba, limites de temperatura.</summary>
    Protective,

    /// <summary>Rotas de áudio, entrada/modo de tela, layout: o que outros ajustes pressupõem.</summary>
    Structural,

    Normal,

    /// <summary>Clocks, potência, tensão: por último, com a proteção já no lugar.</summary>
    Performance,
}

/// <summary>Risco da escrita (A28): alto risco passa por ponto seguro → aplicar → verificar → confirmar.</summary>
public enum RiskLevel
{
    Safe,
    Moderate,
    High,
}

public sealed record ChoiceOption(string Value, string Label);

public readonly record struct CurvePoint(double X, double Y);

/// <summary>Valor de um controle. Serializável (jornal, perfis) com o tipo gravado no JSON.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NumberValue), "number")]
[JsonDerivedType(typeof(ToggleValue), "toggle")]
[JsonDerivedType(typeof(ChoiceValue), "choice")]
[JsonDerivedType(typeof(ColorValue), "color")]
[JsonDerivedType(typeof(CurveValue), "curve")]
[JsonDerivedType(typeof(TextValue), "text")]
public abstract record ControlValue
{
    /// <summary>Mesmo valor dentro da tolerância (o hardware pode arredondar para o passo dele).</summary>
    public abstract bool Matches(ControlValue other, double tolerance);

    public abstract string Display(ControlSpec spec, IFormatProvider provider);
}

public sealed record NumberValue(double Value) : ControlValue
{
    public override bool Matches(ControlValue other, double tolerance) =>
        other is NumberValue n && Math.Abs(n.Value - Value) <= tolerance + 1e-9;

    public override string Display(ControlSpec spec, IFormatProvider provider)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var text = Value.ToString("F" + Math.Clamp(spec.Decimals, 0, 6).ToString(CultureInfo.InvariantCulture), provider);
        return string.IsNullOrEmpty(spec.Unit) ? text : $"{text} {spec.Unit}";
    }
}

public sealed record ToggleValue(bool Value) : ControlValue
{
    public override bool Matches(ControlValue other, double tolerance) => other is ToggleValue t && t.Value == Value;

    public override string Display(ControlSpec spec, IFormatProvider provider) => Value ? "On" : "Off";
}

public sealed record ChoiceValue(string Value) : ControlValue
{
    public override bool Matches(ControlValue other, double tolerance) =>
        other is ChoiceValue c && string.Equals(c.Value, Value, StringComparison.Ordinal);

    public override string Display(ControlSpec spec, IFormatProvider provider)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return spec.Choices.FirstOrDefault(c => string.Equals(c.Value, Value, StringComparison.Ordinal))?.Label ?? Value;
    }
}

public sealed record ColorValue(byte R, byte G, byte B) : ControlValue
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    public override bool Matches(ControlValue other, double tolerance) =>
        other is ColorValue c && Math.Abs(c.R - R) <= tolerance && Math.Abs(c.G - G) <= tolerance && Math.Abs(c.B - B) <= tolerance;

    public override string Display(ControlSpec spec, IFormatProvider provider) => Hex;
}

public sealed record CurveValue(IReadOnlyList<CurvePoint> Points) : ControlValue
{
    public override bool Matches(ControlValue other, double tolerance) =>
        other is CurveValue c && c.Points.Count == Points.Count
        && c.Points.Zip(Points).All(p => Math.Abs(p.First.X - p.Second.X) <= tolerance && Math.Abs(p.First.Y - p.Second.Y) <= tolerance);

    public override string Display(ControlSpec spec, IFormatProvider provider) =>
        string.Join(" · ", Points.Select(p => string.Create(provider, $"{p.X:0.#}→{p.Y:0.#}")));

    public bool Equals(CurveValue? other) => other is not null && Points.SequenceEqual(other.Points);

    public override int GetHashCode() => Points.Aggregate(17, (hash, p) => HashCode.Combine(hash, p));
}

public sealed record TextValue(string Value) : ControlValue
{
    public override bool Matches(ControlValue other, double tolerance) =>
        other is TextValue t && string.Equals(t.Value, Value, StringComparison.Ordinal);

    /// <summary>Textos estruturados (pares do Rapid Tap, ações 2-em-1) têm formato legível próprio no spec.</summary>
    public override string Display(ControlSpec spec, IFormatProvider provider)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return spec.Format?.Invoke(this, provider) ?? Value;
    }
}

/// <summary>
/// Descrição de capacidade de um controle (A25, A26): a interface é gerada a partir disto, com faixa exata, passo,
/// unidade, envelope de segurança e origem do protocolo para o modo avançado (A29).
/// </summary>
public sealed record ControlSpec
{
    public required string Setting { get; init; }

    public required string Label { get; init; }

    public ControlKind Kind { get; init; }

    /// <summary>Grupo na tela gerada por capacidade (ex.: "Actuation", "Lighting").</summary>
    public string Group { get; init; } = string.Empty;

    public string? Description { get; init; }

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public double? Step { get; init; }

    public string Unit { get; init; } = string.Empty;

    public int Decimals { get; init; }

    /// <summary>Envelope de segurança acima de qualquer perfil (ADR-0005 regra 5). Só "Safety" passa por fora.</summary>
    public double? SafeMinimum { get; init; }

    public double? SafeMaximum { get; init; }

    /// <summary>Faixa do eixo X para curvas (temperatura da curva de ventoinha, tensão da curva V/F).</summary>
    public double? XMinimum { get; init; }

    public double? XMaximum { get; init; }

    public IReadOnlyList<ChoiceOption> Choices { get; init; } = [];

    public bool Readable { get; init; } = true;

    public bool Writable { get; init; } = true;

    /// <summary>
    /// Sem protocolo com o dispositivo: o adapter aceita o valor e ele fica só no OneRGB (<c>VendorHidDevice</c>). A
    /// mensagem nunca diz gravado nem confirmado pelo dispositivo.
    /// </summary>
    public bool StoredOnly { get; init; }

    public RiskLevel Risk { get; init; }

    /// <summary>Janela para confirmar uma mudança de alto risco antes da reversão automática.</summary>
    public TimeSpan ConfirmWindow { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    public bool Reversible { get; init; } = true;

    /// <summary>Grava em flash/EEPROM do dispositivo (ADR-0005 regra 3: só quando muda).</summary>
    public bool PersistsOnDevice { get; init; }

    public ControlValue? Default { get; init; }

    /// <summary>Protocolo/API de origem (modo avançado): "DDC/CI VCP 0x10", "NVML", "HID feature 0x06".</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Faixa bruta do protocolo (modo avançado), ex. "0x00–0x64".</summary>
    public string? RawRange { get; init; }

    public ApplyPhase Phase { get; init; } = ApplyPhase.Normal;

    /// <summary>Ajustes do mesmo recurso que precisam ser aplicados antes (ex.: "hdr" depois de "mode").</summary>
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    /// <summary>
    /// Mudar este ajuste troca o que os outros do mesmo recurso leem e gravam (o perfil da memória do mouse, B10): depois
    /// dele, os outros vão de novo mesmo que o perfil do OneRGB não tenha mudado, e editar só ele leva os outros junto.
    /// </summary>
    public bool SwitchesResource { get; init; }

    /// <summary>
    /// Regra do domínio além de faixa e passo (A26): curva V/F crescente, pares do Rapid Tap sem tecla repetida,
    /// ação 2-em-1 com o segundo ponto mais fundo. Recebe o valor já normalizado; devolve o motivo da recusa.
    /// </summary>
    public Func<ControlValue, string?>? Check { get; init; }

    /// <summary>Texto legível para valores de texto estruturado (a UI e o jornal nunca mostram a codificação crua).</summary>
    public Func<TextValue, IFormatProvider, string>? Format { get; init; }

    /// <summary>Por que o valor aparece assim quando a fonte não foi conferida no hardware (A20, "a confirmar").</summary>
    public string? Evidence { get; init; }

    /// <summary>Tolerância da verificação: meio passo, ou 0,5% da faixa, ou 0 para valores discretos.</summary>
    public double Tolerance => Kind switch
    {
        ControlKind.Number or ControlKind.Curve => Step is > 0 ? Step.Value / 2 : Minimum is { } min && Maximum is { } max ? (max - min) * 0.005 : 0,
        ControlKind.Color => 1,
        _ => 0,
    };
}

/// <summary>
/// Adapter de um controle (A18): a única peça que fala com o hardware. Não decide nada — validação, posse,
/// jornal e verificação ficam no <see cref="ControlService"/>.
/// </summary>
public interface IControlAdapter
{
    /// <summary>Mesmo objeto = ajustes que o dispositivo aceita no mesmo relatório. Nulo mantém a escrita individual.</summary>
    object? BatchGroup => null;

    Task WriteBatchAsync(IReadOnlyDictionary<ControlKey, ControlValue> values, Lease lease, CancellationToken cancellationToken) =>
        Task.FromException(new NotSupportedException("This adapter does not support batched writes."));

    ControlKey Key { get; }

    ControlSpec Spec { get; }

    /// <summary>Dispositivo presente e adapter pronto.</summary>
    bool IsAvailable { get; }

    /// <summary>Escrita liberada (ADR-0005: só com afirmações verified_hw; chave para desligar).</summary>
    bool WriteEnabled { get; }

    string? WriteDisabledReason { get; }

    /// <summary>Valor atual no dispositivo; <c>null</c> se o protocolo não permite leitura.</summary>
    Task<ControlValue?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Escreve com a posse concedida pelo árbitro; o adapter confere <see cref="ControlArbiter.IsCurrent"/>.</summary>
    Task WriteAsync(ControlValue value, Lease lease, CancellationToken cancellationToken);
}

public sealed record ControlRequest(
    ControlKey Key,
    ControlValue Value,
    string Writer,
    WriterPriority Priority = WriterPriority.TemporaryUserOverride,
    string? Reason = null);

public enum CommandOutcome
{
    Verified,
    Applied,
    AwaitingConfirmation,
    Superseded,
    Rejected,
    Blocked,
    Failed,
    RolledBack,
    Cancelled,
}

public sealed record CommandResult(string OperationId, ControlKey Key, CommandOutcome Outcome, ControlValue? Before, ControlValue? After, string Message)
{
    public bool Succeeded => Outcome is CommandOutcome.Verified or CommandOutcome.Applied or CommandOutcome.AwaitingConfirmation;
}

/// <summary>
/// Estado de um controle para a interface (A15.4, A27): desejado (o que o usuário pediu agora), aplicado (o que o
/// OneRGB escreveu), observado (o que o dispositivo diz), persistente (o que o perfil salva) e se é temporário.
/// </summary>
public sealed record ControlStatus(
    ControlKey Key,
    ControlSpec Spec,
    FeatureState State,
    ControlValue? Desired,
    ControlValue? Applied,
    ControlValue? Observed,
    ControlValue? Persistent,
    string? Message,
    string? Owner,
    string? LastWriter,
    DateTimeOffset? LastWrite,
    DateTimeOffset? ConfirmBy)
{
    public bool Pending => State is FeatureState.Applying;

    public bool AwaitingConfirmation => ConfirmBy is not null;

    /// <summary>Por que a escrita está desligada neste controle (ADR-0005); nulo quando ele aceita escrita.</summary>
    public string? ReadOnlyReason { get; init; }

    /// <summary>O dispositivo responde agora; falso deixa o último valor lido só como histórico (bateria some do card).</summary>
    public bool Available { get; init; } = true;

    /// <summary>Aplicado difere do salvo no perfil: mudança temporária.</summary>
    public bool Temporary => Applied is not null && Persistent is not null && !Applied.Matches(Persistent, Spec.Tolerance);
}

/// <summary>Mudança planejada, para prévia antes de aplicar (ADR-0005 "draft/preview", A24 prévia de migração).</summary>
public sealed record PlannedChange(ControlKey Key, string Label, ControlValue? Current, ControlValue Target, RiskLevel Risk, bool Changes, string? Blocked);
