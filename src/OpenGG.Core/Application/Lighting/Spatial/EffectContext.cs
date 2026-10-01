namespace OneRGB.Application.Lighting.Spatial;

/// <summary>The existing WASAPI capture only needs audio level and spectrum bands in this application.</summary>
public sealed class EffectContext
{
    public const int BandCount = 16;
    private double _audioLevel;
    public double AudioLevel { get => Volatile.Read(ref _audioLevel); set => Volatile.Write(ref _audioLevel, Math.Clamp(value, 0, 1)); }
    public void SetBands(ReadOnlySpan<double> bands) { } // Spectrum data is currently unused by the six imported effects.
}
