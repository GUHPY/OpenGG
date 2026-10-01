using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OneRGB.Application.Lighting;
using OneRGB.Domain;

namespace OpenGG.Desktop.Controls;

/// <summary>
/// Visualização do teclado SteelSeries Apex Pro TKL Gen 3 a partir das camadas do PSD original:
/// 1. Base Exterior
/// 2. Base Interior
/// 3. Borda Keycaps
/// 4. Camada de Efeito RGB dinâmico (com glow difuso, animação procedural e iluminação sob as teclas)
/// 5. Keycaps (com legendas e vãos translúcidos)
/// 6. Tela OLED
/// 7. Roda Scroll
/// </summary>
public sealed class ApexKeyboard : FrameworkElement
{
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(LightingMode), typeof(ApexKeyboard),
        new FrameworkPropertyMetadata(LightingMode.Wave, FrameworkPropertyMetadataOptions.AffectsRender, OnModeChanged));

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(Color), typeof(ApexKeyboard),
        new FrameworkPropertyMetadata(Color.FromRgb(0x06, 0xB6, 0xD4), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BrightnessProperty = DependencyProperty.Register(
        nameof(Brightness), typeof(double), typeof(ApexKeyboard),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SpeedProperty = DependencyProperty.Register(
        nameof(Speed), typeof(double), typeof(ApexKeyboard),
        new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlowProperty = DependencyProperty.Register(
        nameof(Glow), typeof(bool), typeof(ApexKeyboard),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AudioLevelProperty = DependencyProperty.Register(
        nameof(AudioLevel), typeof(double), typeof(ApexKeyboard),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OledImageProperty = DependencyProperty.Register(
        nameof(OledImage), typeof(ImageSource), typeof(ApexKeyboard),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ZoomProgressProperty = DependencyProperty.Register(
        nameof(ZoomProgress), typeof(double), typeof(ApexKeyboard),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OledFocusedProperty = DependencyProperty.Register(
        nameof(OledFocused), typeof(bool), typeof(ApexKeyboard),
        new PropertyMetadata(false, (d, e) => Motion.To((ApexKeyboard)d, ZoomProgressProperty,
            (bool)e.NewValue ? 1 : 0, Motion.Deliberate, CubicBezierEase.Emphasized)));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(ApexKeyboard),
        new FrameworkPropertyMetadata(true, OnIsActiveChanged));

    // Dimensões nativas do PSD do Apex Pro TKL
    private const double NativeWidth = 1536.0;
    private const double NativeHeight = 1024.0;

    // Região do plate / iluminação sob as teclas (Base Interior: bbox 83, 158 a 1470, 621)
    private static readonly Rect PlateRect = new(79, 155, 1362, 470);
    private static readonly Rect OledRect = new(1217, 158, 171, 92);

    // Camadas de imagem estáticas em memória (carregadas sob demanda e congeladas)
    private static BitmapImage? _baseExterior;
    private static BitmapImage? _baseInterior;
    private static BitmapImage? _bordaKeycaps;
    private static BitmapImage? _keycaps;
    private static BitmapImage? _oled;
    private static BitmapImage? _scroll;
    private static BitmapImage? _lightMask;
    private static ImageBrush? _lightMaskBrush;
    private static bool _assetsLoaded;
    private static readonly Lock AssetLock = new();

    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _phase;
    private double _lastSeconds;

    public ApexKeyboard()
    {
        EnsureAssetsLoaded();

        _timer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(25)
        };
        _timer.Tick += OnTimerTick;
        Loaded += (_, _) => UpdateTimerState();
        Unloaded += (_, _) => _timer.Stop();
        IsVisibleChanged += (_, _) => UpdateTimerState();
    }

    public LightingMode Mode
    {
        get => (LightingMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public double Brightness
    {
        get => (double)GetValue(BrightnessProperty);
        set => SetValue(BrightnessProperty, value);
    }

    public double Speed
    {
        get => (double)GetValue(SpeedProperty);
        set => SetValue(SpeedProperty, value);
    }

    public bool Glow
    {
        get => (bool)GetValue(GlowProperty);
        set => SetValue(GlowProperty, value);
    }

    public double AudioLevel
    {
        get => (double)GetValue(AudioLevelProperty);
        set => SetValue(AudioLevelProperty, value);
    }

    public ImageSource? OledImage { get => (ImageSource?)GetValue(OledImageProperty); set => SetValue(OledImageProperty, value); }
    public bool OledFocused { get => (bool)GetValue(OledFocusedProperty); set => SetValue(OledFocusedProperty, value); }
    public double ZoomProgress => (double)GetValue(ZoomProgressProperty);

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ApexKeyboard kb)
        {
            kb.UpdateTimerState();
        }
    }

    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ApexKeyboard kb)
        {
            kb.UpdateTimerState();
        }
    }

    private void UpdateTimerState()
    {
        var shouldRun = IsLoaded && IsVisible && IsActive && Mode != LightingMode.Off;
        if (shouldRun)
        {
            if (!_timer.IsEnabled)
            {
                _lastSeconds = _clock.Elapsed.TotalSeconds;
                _timer.Start();
            }
        }
        else
        {
            _timer.Stop();
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var dt = now - _lastSeconds;
        _lastSeconds = now;

        if (dt <= 0 || dt > 0.5)
        {
            dt = 0.02;
        }

        // Velocidade da onda / pulso / ciclo
        var speedFactor = 0.2 + (Math.Clamp(Speed, 0, 1) * 1.8);
        _phase += dt * speedFactor;

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        EnsureAssetsLoaded();

        // Resting margins belong to the camera, not to a smaller clipped Viewbox.
        var restingWidth = Math.Max(1, ActualWidth - 32);
        var restingHeight = Math.Max(1, ActualHeight - 84);
        var scale = Math.Min(restingWidth / NativeWidth, restingHeight / NativeHeight);
        if (scale <= 0)
        {
            return;
        }

        var ox = (ActualWidth - (NativeWidth * scale)) / 2.0;
        var oy = 48 + (restingHeight - (NativeHeight * scale)) / 2.0;

        var zoom = Math.Clamp(ZoomProgress, 0, 1);
        var closeScale = Math.Min(ActualWidth / (OledRect.Width * 1.3), ActualHeight / (OledRect.Height * 2));
        ox += (ActualWidth / 2 - (OledRect.Left + OledRect.Width / 2) * closeScale - ox) * zoom;
        oy += (ActualHeight / 2 - (OledRect.Top + OledRect.Height / 2) * closeScale - oy) * zoom;
        scale += (closeScale - scale) * zoom;

        drawingContext.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), 16, 16));
        drawingContext.PushTransform(new TranslateTransform(ox, oy));
        drawingContext.PushTransform(new ScaleTransform(scale, scale));

        var fullRect = new Rect(0, 0, NativeWidth, NativeHeight);

        // 1. Camada: Base Exterior (chassi exterior e apoio de pulso)
        if (_baseExterior is not null)
        {
            drawingContext.DrawImage(_baseExterior, fullRect);
        }

        // 2. Camada: Base Interior (placa de montagem interna)
        if (_baseInterior is not null)
        {
            drawingContext.DrawImage(_baseInterior, fullRect);
        }

        // 3. Camada: Borda Keycaps (molduras e alojamento dos switches)
        if (_bordaKeycaps is not null)
        {
            drawingContext.DrawImage(_bordaKeycaps, fullRect);
        }

        // 4. Camada de Efeito RGB dinâmico (com difusão física, glow e vazamento sobre a carcaça)
        DrawRgbEffectLayer(drawingContext, fullRect);

        // 5. Camada: Keycaps (teclas com legendas vazadas e espaçamento translúcido)
        if (_keycaps is not null)
        {
            drawingContext.DrawImage(_keycaps, fullRect);
        }

        // 6. Camada: OLED (display monocromático com visual ativo)
        if (_oled is not null)
        {
            drawingContext.DrawImage(_oled, fullRect);
        }
        DrawOledDisplay(drawingContext);

        // 7. Camada: Scroll (rolo de volume de metal)
        if (_scroll is not null)
        {
            drawingContext.DrawImage(_scroll, fullRect);
        }

        drawingContext.Pop(); // Scale
        drawingContext.Pop(); // Translate
        drawingContext.Pop(); // Viewport
    }

    /// <summary>
    /// Desenha o efeito RGB com difusão física, glow suave vazando para a carcaça exterior
    /// e realce vibrante dentro dos switches e das legendas vazadas das keycaps.
    /// </summary>
    private void DrawRgbEffectLayer(DrawingContext dc, Rect fullRect)
    {
        if (Mode == LightingMode.Off)
        {
            return;
        }

        var brightness = Math.Clamp(Brightness, 0.0, 1.0);
        if (brightness <= 0.001)
        {
            return;
        }

        // Preserve the original OneRGB continuous spectrum. Hardware frames are ordered by key,
        // not by horizontal position, and their already-dimmed RGB bytes are not preview opacity.
        var effectBrush = CreateEffectBrush(brightness);

        if (!Glow)
        {
            // Sem halo difuso: restringe iluminação ao contorno do plate interno
            dc.PushClip(new RectangleGeometry(PlateRect, 10, 10));
        }

        if (_lightMaskBrush is not null)
        {
            // Iluminação física dispersa: banha a base metálica e as bordas de forma translúcida,
            // vaza suavemente para a carcaça exterior e destaca com nitidez as teclas e legendas.
            dc.PushOpacityMask(_lightMaskBrush);
            dc.DrawRectangle(effectBrush, null, fullRect);
            dc.Pop();
        }
        else
        {
            dc.DrawRoundedRectangle(effectBrush, null, PlateRect, 12, 12);
        }

        if (!Glow)
        {
            dc.Pop();
        }
    }

    private Brush CreateEffectBrush(double opacity)
    {
        var time = _clock.Elapsed.TotalSeconds;

        switch (Mode)
        {
            case LightingMode.Wave:
            default:
            {
                // Onda Rainbow contínua e suave da esquerda para a direita
                var p = (_phase * 0.45) % 1.0;
                var brush = new LinearGradientBrush
                {
                    StartPoint = new Point(p - 1.0, 0),
                    EndPoint = new Point(p, 0),
                    SpreadMethod = GradientSpreadMethod.Repeat,
                    MappingMode = BrushMappingMode.RelativeToBoundingBox,
                    Opacity = opacity
                };

                // Espectro arco-íris vibrante (curado para parecer iluminação de hardware real)
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x1E, 0x38), 0.00));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x78, 0x00), 0.16));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xD6, 0x00), 0.32));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x00, 0xE6, 0x76), 0.48));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x00, 0xE5, 0xFF), 0.64));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x3B, 0x82, 0xF6), 0.80));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x9C, 0x27, 0xB0), 0.92));
                brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x1E, 0x38), 1.00));

                brush.Freeze();
                return brush;
            }

            case LightingMode.ColorCycle:
            {
                // Ciclo suave de todas as cores HSL no tempo
                var hue = ((time * 45.0 * (0.3 + (Speed * 1.5))) + (_phase * 20.0)) % 360.0;
                var rgb = Rgb.FromHsv(hue, 1.0, 1.0);
                var c = Color.FromRgb(rgb.R, rgb.G, rgb.B);
                var brush = new SolidColorBrush(c) { Opacity = opacity };
                brush.Freeze();
                return brush;
            }

            case LightingMode.Breathing:
            {
                // Pulsação suave em seno
                var period = 4.0 / Math.Max(0.1, 0.2 + (Speed * 1.8));
                var factor = 0.12 + (0.88 * (0.5 - (0.5 * Math.Cos((2.0 * Math.PI * time) / period))));
                var brush = new SolidColorBrush(Color) { Opacity = opacity * factor };
                brush.Freeze();
                return brush;
            }

            case LightingMode.AudioReactive:
            {
                // Reage ao volume de áudio ou pulsação viva
                var factor = OneRGB.Application.Lighting.AudioLight.Level(AudioLevel);
                var brush = new SolidColorBrush(Color) { Opacity = opacity * factor };
                brush.Freeze();
                return brush;
            }

            case LightingMode.Static:
            {
                // Cor estática escolhida pelo usuário
                var brush = new SolidColorBrush(Color) { Opacity = opacity };
                brush.Freeze();
                return brush;
            }
        }
    }

    private void DrawOledDisplay(DrawingContext dc)
    {
        if (OledImage is null) return;
        var width = OledRect.Width - 12;
        var height = width * 40 / 128;
        var drawing = new DrawingGroup();
        drawing.Children.Add(new ImageDrawing(OledImage, new Rect(OledRect.Left + 6,
            OledRect.Top + (OledRect.Height - height) / 2, width, height)));
        RenderOptions.SetBitmapScalingMode(drawing, BitmapScalingMode.NearestNeighbor);
        drawing.Freeze();
        dc.DrawDrawing(drawing);
    }

    private static void EnsureAssetsLoaded()
    {
        if (_assetsLoaded)
        {
            return;
        }

        lock (AssetLock)
        {
            if (_assetsLoaded)
            {
                return;
            }

            _baseExterior = LoadAsset("base_exterior.png");
            _baseInterior = LoadAsset("base_interior.png");
            _bordaKeycaps = LoadAsset("borda_keycaps.png");
            _keycaps = LoadAsset("keycaps.png");
            _oled = LoadAsset("oled.png");
            _scroll = LoadAsset("scroll.png");
            _lightMask = LoadAsset("light_mask.png");
            if (_lightMask is not null)
            {
                var brush = new ImageBrush(_lightMask);
                brush.Freeze();
                _lightMaskBrush = brush;
            }
            _assetsLoaded = true;
        }
    }

    private static BitmapImage? LoadAsset(string filename)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/OpenGG;component/Assets/Devices/Apex/{filename}", UriKind.Absolute);
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = uri;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
