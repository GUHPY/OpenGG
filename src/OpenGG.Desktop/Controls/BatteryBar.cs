using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OpenGG.Desktop.Controls;

public sealed class BatteryBar : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(nameof(Percent), typeof(int?), typeof(BatteryBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, Changed));
    public static readonly DependencyProperty ChargingProperty = DependencyProperty.Register(nameof(Charging), typeof(bool), typeof(BatteryBar), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, Changed));
    private static readonly DependencyProperty PhaseProperty = DependencyProperty.Register("Phase", typeof(double), typeof(BatteryBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly Brush Green = Frozen("#71D89B"), Yellow = Frozen("#E6C768"), Red = Frozen("#F07878"), Track = Frozen("#35353A");
    private static readonly Geometry Bolt = Geometry.Parse("M8,0 L2,8 H6 L4,14 L12,5 H8 Z");
    public int? Percent { get => (int?)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public bool Charging { get => (bool)GetValue(ChargingProperty); set => SetValue(ChargingProperty, value); }
    public BatteryBar() { Height = 16; Loaded += (_, _) => Animate(); Unloaded += (_, _) => BeginAnimation(PhaseProperty, null); }
    private static SolidColorBrush Frozen(string hex) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); return brush; }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BatteryBar)d).Animate();
    /// <summary>No carregador e ainda enchendo; em 100 % o raio fica parado (carregado).</summary>
    private bool Filling => Charging && Percent is < 100;
    private void Animate() => BeginAnimation(PhaseProperty, IsLoaded && Filling && Motion.Enabled
        ? new DoubleAnimation(-.3, 1.3, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever } : null);
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Percent is not { } percent || ActualWidth <= 0) return;
        var color = Charging || percent >= 50 ? Green : percent >= 20 ? Yellow : Red;
        var width = Math.Max(0, ActualWidth - (Charging ? 22 : 0));
        var track = new Rect(0, 5, width, 6);
        drawingContext.DrawRoundedRectangle(Track, null, track, 3, 3);
        var fill = new Rect(0, 5, width * Math.Clamp(percent, 0, 100) / 100.0, 6);
        drawingContext.DrawRoundedRectangle(color, null, fill, 3, 3);
        if (!Charging) return;
        drawingContext.PushTransform(new TranslateTransform(ActualWidth - 14, 1)); drawingContext.DrawGeometry(Green, null, Bolt); drawingContext.Pop();
        if (!Filling) return;
        drawingContext.PushClip(new RectangleGeometry(fill, 3, 3));
        drawingContext.PushOpacity(.45);
        drawingContext.DrawRoundedRectangle(Brushes.White, null, new Rect(width * (double)GetValue(PhaseProperty), 5, Math.Max(12, width * .22), 6), 3, 3);
        drawingContext.Pop(); drawingContext.Pop();
    }
}
