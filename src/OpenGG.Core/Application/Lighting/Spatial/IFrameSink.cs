using System.Globalization;
using OneRGB.Application.Control;

namespace OneRGB.Application.Lighting.Spatial;

/// <summary>
/// Saída de quadros de um dispositivo com LED (Etapa 4.5). Quadros contínuos não passam pelo jornal: o motor entrega
/// direto, sempre com a posse vigente do recurso no <see cref="ControlArbiter"/>. O adaptador confere a posse, copia o
/// quadro e envia no ritmo dele — <see cref="Submit"/> não pode bloquear.
/// </summary>
public interface IFrameSink
{
    string DeviceId { get; }

    /// <summary>Recurso de RGB do dispositivo (o mesmo dos controles dele), disputado no árbitro.</summary>
    ResourceId Resource { get; }

    bool IsAvailable { get; }

    /// <summary>Escrita liberada (ADR-0005: protocolo conferido e opt-in do usuário).</summary>
    bool WriteEnabled { get; }

    string? WriteDisabledReason { get; }

    void Submit(Lease lease, ReadOnlySpan<Rgb> frame);

    /// <summary>O motor parou de usar o dispositivo: ele volta ao próprio estado (efeito salvo, cena do app).</summary>
    void Release();
}

