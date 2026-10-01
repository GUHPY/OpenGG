using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using OneRGB.Application.Lighting;

namespace OpenGG.Desktop.Controls;

/// <summary>
/// Seletor HSV (quadrado saturação × brilho + barra de matiz) — substitui o &lt;input type="color"&gt; do
/// RgbColorPicker.tsx por um controle nativo com arraste. Color é bindável nos dois sentidos.
/// </summary>
public sealed class HsvPicker : Grid
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Color), typeof(HsvPicker),
        new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnColorChanged));

    private readonly Border _square;
    private readonly Border _hueBar;
    private readonly Ellipse _svThumb;
    private readonly Border _hueThumb;
    private readonly SolidColorBrush _hueBrush = new(Colors.Red);
    private double _h, _s = 1, _v = 1;
    private bool _updating;

    public HsvPicker()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });

        var white = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0);
        var black = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90);
        var layers = new Grid();
        layers.Children.Add(new Border { Background = _hueBrush, CornerRadius = new CornerRadius(8) });
        layers.Children.Add(new Border { Background = white, CornerRadius = new CornerRadius(8) });
        layers.Children.Add(new Border { Background = black, CornerRadius = new CornerRadius(8) });
        _svThumb = new Ellipse
        {
            Width = 16,
            Height = 16,
            Stroke = Brushes.White,
            StrokeThickness = 2.5,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.7 },
        };
        layers.Children.Add(_svThumb);
        _square = new Border { Child = layers, CornerRadius = new CornerRadius(8), Cursor = Cursors.Cross, Background = Brushes.Transparent, ClipToBounds = false };
        SetRow(_square, 0);
        Children.Add(_square);

        var hueGrid = new Grid();
        hueGrid.Children.Add(new Border { Background = FindHue(), CornerRadius = new CornerRadius(7) });
        _hueThumb = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2.5),
            HorizontalAlignment = HorizontalAlignment.Left,
            IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = 0.7 },
        };
        hueGrid.Children.Add(_hueThumb);
        _hueBar = new Border { Child = hueGrid, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        SetRow(_hueBar, 2);
        Children.Add(_hueBar);

        Hook(_square, OnSquare);
        Hook(_hueBar, OnHue);
        SizeChanged += (_, _) => PlaceThumbs();
        SyncFromColor(Color);
    }

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    private static LinearGradientBrush FindHue()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        string[] stops = ["#FF0000", "#FFFF00", "#00FF00", "#00FFFF", "#0000FF", "#FF00FF", "#FF0000"];
        for (var i = 0; i < stops.Length; i++)
        {
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(stops[i]), i / 6.0));
        }

        brush.Freeze();
        return brush;
    }

    private static void Hook(FrameworkElement element, Action<Point> handler)
    {
        element.MouseLeftButtonDown += (s, e) =>
        {
            element.CaptureMouse();
            handler(e.GetPosition(element));
            e.Handled = true;
        };
        element.MouseMove += (s, e) =>
        {
            if (element.IsMouseCaptured)
            {
                handler(e.GetPosition(element));
            }
        };
        element.MouseLeftButtonUp += (s, e) => element.ReleaseMouseCapture();
    }

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (HsvPicker)d;
        if (!picker._updating)
        {
            picker.SyncFromColor((Color)e.NewValue);
        }
    }

    private void OnSquare(Point p)
    {
        _s = Math.Clamp(p.X / Math.Max(1, _square.ActualWidth), 0, 1);
        _v = 1 - Math.Clamp(p.Y / Math.Max(1, _square.ActualHeight), 0, 1);
        Commit();
    }

    private void OnHue(Point p)
    {
        _h = Math.Clamp(p.X / Math.Max(1, _hueBar.ActualWidth), 0, 1) * 360;
        Commit();
    }

    private void Commit()
    {
        var rgb = Rgb.FromHsv(_h, _s, _v);
        _updating = true;
        Color = Color.FromRgb(rgb.R, rgb.G, rgb.B);
        _updating = false;
        PlaceThumbs();
    }

    private void SyncFromColor(Color color)
    {
        var (h, s, v) = new Rgb(color.R, color.G, color.B).ToHsv();
        if (s > 0.001 && v > 0.001)
        {
            _h = h;
        }

        _s = s;
        _v = v;
        PlaceThumbs();
    }

    private void PlaceThumbs()
    {
        var hue = Rgb.FromHsv(_h, 1, 1);
        _hueBrush.Color = Color.FromRgb(hue.R, hue.G, hue.B);
        var current = Rgb.FromHsv(_h, _s, _v);
        _svThumb.Fill = new SolidColorBrush(Color.FromRgb(current.R, current.G, current.B));
        _svThumb.Margin = new Thickness((_s * _square.ActualWidth) - 8, ((1 - _v) * _square.ActualHeight) - 8, 0, 0);
        _hueThumb.Background = new SolidColorBrush(Color.FromRgb(hue.R, hue.G, hue.B));
        _hueThumb.Margin = new Thickness((_h / 360 * _hueBar.ActualWidth) - 7, 0, 0, 0);
    }
}
