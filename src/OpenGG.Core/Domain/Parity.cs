namespace OneRGB.Domain;

/// <summary>Estados de paridade exigidos pela spec §4.</summary>
public enum ParityStatus
{
    Full,
    Superset,
    Partial,
    BlockedByVendor,
    BlockedByWindows,
    RequiresDriver,
    RequiresPrivilege,
    ResearchRequired,
    NotApplicable,

    /// <summary>Melhor que o original, além de equivalente (A19 "Superior").</summary>
    Superior,

    /// <summary>O hardware do usuário não tem o recurso (A19 "Hardware-blocked").</summary>
    BlockedByHardware,

    /// <summary>Só via app/serviço de terceiros instalado, explicado ao usuário (A19, A23).</summary>
    CompatibilityBridge,
}

/// <summary>Força da evidência de um requisito derivado dos vídeos (Apêndice A, "Evidence model").</summary>
public enum EvidenceKind
{
    Video,
    WebVerified,
    WebExtended,
    Inferred,
    Blocked,
}

/// <summary>Progresso real de implementação — separado do estado-alvo de paridade.</summary>
public enum ImplementationStatus
{
    NotStarted,
    InProgress,
    Implemented,
    Verified,
}

/// <summary>Uma linha do registro de paridade (phase0/data/parity.json).</summary>
public sealed record ParityFeature(
    string Id,
    string SourceApp,
    string DeviceId,
    string Feature,
    string Behavior,
    string OneRgbPath,
    string Privilege,
    string Driver,
    string Openness,
    ParityStatus ParityStatus,
    ImplementationStatus ImplementationStatus,
    int Phase,
    bool InventoryVerifiedAgainstApp,
    string Notes)
{
    /// <summary>Evidência do requisito (vazio para as linhas herdadas da Fase 0).</summary>
    public IReadOnlyList<EvidenceKind> Evidence { get; init; } = [];

    /// <summary>Seção do protocolo de vídeo (ex.: "A5.8").</summary>
    public string ProtocolSection { get; init; } = string.Empty;

    /// <summary>Arquivo de vídeo de origem, quando houver.</summary>
    public string? Video { get; init; }

    public string KnownLimitation { get; init; } = string.Empty;

    /// <summary>IDs antigos do registro que este requisito detalha.</summary>
    public IReadOnlyList<string> Related { get; init; } = [];

    /// <summary>Checklist A19: ui, behavior, persistence, hardware_effect, profile, error_handling, reboot, hotplug, verification, regression_test.</summary>
    public IReadOnlyDictionary<string, bool> Done { get; init; } = new Dictionary<string, bool>(StringComparer.Ordinal);

    public bool FromVideoProtocol => Evidence.Count > 0;

    /// <summary>A19: só está pronto com os 10 itens do checklist.</summary>
    public bool CompletelyDone => Done.Count >= 10 && Done.Values.All(v => v);

    public int DoneCount => Done.Values.Count(v => v);

    /// <summary>
    /// Uma função nunca some da interface: quando não está disponível, a UI mostra este motivo (spec §506).
    /// </summary>
    public string UnavailableReason => ParityStatus switch
    {
        ParityStatus.BlockedByVendor => "Blocked by the vendor: use the official tool.",
        ParityStatus.BlockedByWindows => "Windows does not expose this feature.",
        ParityStatus.RequiresDriver => $"Exige driver: {Driver}.",
        ParityStatus.RequiresPrivilege => "Requires OneRGB's privileged service.",
        ParityStatus.ResearchRequired => "Protocol is still under investigation.",
        ParityStatus.NotApplicable => "Does not apply to this hardware.",
        ParityStatus.BlockedByHardware => "Your hardware does not have this feature.",
        ParityStatus.CompatibilityBridge => string.IsNullOrWhiteSpace(KnownLimitation)
            ? "Uses the installed vendor application as a compatibility bridge."
            : $"Compatibility bridge: {KnownLimitation}",
        _ => ImplementationStatus is ImplementationStatus.NotStarted or ImplementationStatus.InProgress
            ? "Not implemented yet."
            : string.Empty,
    };
}

/// <summary>Resumo para o painel de cobertura de funcionalidades (spec §169).</summary>
public sealed record ParityCoverage(
    int Total,
    int InventoryVerified,
    IReadOnlyDictionary<ParityStatus, int> ByStatus,
    IReadOnlyDictionary<ImplementationStatus, int> ByImplementation,
    IReadOnlyDictionary<string, int> BySourceApp)
{
    public static ParityCoverage From(IReadOnlyCollection<ParityFeature> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        return new ParityCoverage(
            features.Count,
            features.Count(f => f.InventoryVerifiedAgainstApp),
            features.GroupBy(f => f.ParityStatus).ToDictionary(g => g.Key, g => g.Count()),
            features.GroupBy(f => f.ImplementationStatus).ToDictionary(g => g.Key, g => g.Count()),
            features.GroupBy(f => f.SourceApp, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal));
    }
}
