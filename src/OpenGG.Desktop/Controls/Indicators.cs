using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OneRGB.Application.Commands;

namespace OpenGG.Desktop.Controls;

public enum IndicatorShape
{
    /// <summary>Preenche o item escolhido (segmented control, boardui).</summary>
    Fill,

    /// <summary>Linha embaixo do item (abas).</summary>
    Underline,

    /// <summary>Barra fina à esquerda do item (navegação lateral, microkit spotlight-indicator).</summary>
    Bar,
}

/// <summary>
/// Indicador que desliza até o item selecionado (porte de boardui/segmented-control e microkit/spotlight-indicator,
/// MIT): o elemento com <c>SlidingIndicator.Shape</c> dentro do template de um <see cref="Selector"/> acompanha a
/// seleção com uma transição de 250 ms. Em vez de cada item pintar o próprio fundo, um único elemento se move —
/// "movimento conectado" (spec §75). Com "Reduzir movimento", salta direto.
/// </summary>
public static class SlidingIndicator
{
    public static readonly DependencyProperty ShapeProperty = DependencyProperty.RegisterAttached(
        "Shape", typeof(IndicatorShape?), typeof(SlidingIndicator), new PropertyMetadata(null, OnShapeChanged));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(Tracker), typeof(SlidingIndicator), new PropertyMetadata(null));

    public static IndicatorShape? GetShape(DependencyObject d) => (IndicatorShape?)d.GetValue(ShapeProperty);

    public static void SetShape(DependencyObject d, IndicatorShape? value) => d.SetValue(ShapeProperty, value);

    private static void OnShapeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement indicator || e.NewValue is null || indicator.GetValue(StateProperty) is Tracker)
        {
            return;
        }

        var tracker = new Tracker(indicator);
        indicator.SetValue(StateProperty, tracker);
        indicator.Loaded += (_, _) => tracker.Attach();
        indicator.Unloaded += (_, _) => tracker.Detach();
    }

    private sealed class Tracker(FrameworkElement indicator)
    {
        private Selector? _selector;
        private Rect? _last;

        public void Attach()
        {
            _selector = indicator.TemplatedParent as Selector ?? FindSelector(indicator);
            if (_selector is null)
            {
                return;
            }

            indicator.HorizontalAlignment = HorizontalAlignment.Left;
            indicator.VerticalAlignment = VerticalAlignment.Top;
            indicator.IsHitTestVisible = false;
            _selector.SelectionChanged += OnSelectionChanged;
            _selector.LayoutUpdated += OnLayoutUpdated;
            Move(animate: false);
        }

        public void Detach()
        {
            if (_selector is null)
            {
                return;
            }

            _selector.SelectionChanged -= OnSelectionChanged;
            _selector.LayoutUpdated -= OnLayoutUpdated;
            _selector = null;
            _last = null;
        }

        private static Selector? FindSelector(DependencyObject start)
        {
            for (var node = VisualTreeHelper.GetParent(start); node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is Selector selector)
                {
                    return selector;
                }
            }

            return null;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => Move(animate: true);

        // Janela redimensionada, fonte carregada, item entrando: reposiciona sem animar.
        private void OnLayoutUpdated(object? sender, EventArgs e) => Move(animate: false, onlyIfMoved: true);

        private void Move(bool animate, bool onlyIfMoved = false)
        {
            if (_selector is null || indicator.Parent is not UIElement host)
            {
                return;
            }

            var container = _selector.SelectedItem is null ? null
                : _selector.ItemContainerGenerator.ContainerFromItem(_selector.SelectedItem) as FrameworkElement;
            if (container is null || !container.IsVisible || !host.IsAncestorOf(container))
            {
                indicator.Opacity = 0;
                _last = null;
                return;
            }

            var bounds = container.TransformToAncestor(host).TransformBounds(new Rect(container.RenderSize));
            if (onlyIfMoved && _last is { } last && Near(last, bounds))
            {
                return;
            }

            var first = _last is null;
            _last = bounds;
            var shape = GetShape(indicator) ?? IndicatorShape.Fill;
            var (x, y, width, height) = shape switch
            {
                IndicatorShape.Underline => (bounds.X, bounds.Bottom - Math.Max(indicator.Height is > 0 and var h ? h : 2, 1), bounds.Width, double.NaN),
                IndicatorShape.Bar => (bounds.X, bounds.Y + 8, double.NaN, Math.Max(bounds.Height - 16, 4)),
                _ => (bounds.X, bounds.Y, bounds.Width, bounds.Height),
            };
            var translate = indicator.RenderTransform as TranslateTransform;
            if (translate is null || translate.IsFrozen)
            {
                translate = new TranslateTransform();
                indicator.RenderTransform = translate;
            }

            var smooth = animate && !first && Motion.Enabled;
            indicator.Opacity = 1;
            Apply(translate, TranslateTransform.XProperty, x, smooth);
            Apply(translate, TranslateTransform.YProperty, y, smooth);
            if (!double.IsNaN(width))
            {
                Apply(indicator, FrameworkElement.WidthProperty, width, smooth);
            }

            if (!double.IsNaN(height))
            {
                Apply(indicator, FrameworkElement.HeightProperty, height, smooth);
            }
        }

        private static void Apply(IAnimatable target, DependencyProperty property, double value, bool smooth)
        {
            if (!smooth)
            {
                target.BeginAnimation(property, null);
                ((DependencyObject)target).SetValue(property, value);
                return;
            }

            target.BeginAnimation(property, new DoubleAnimation(value, new Duration(Motion.Standard)) { EasingFunction = CubicBezierEase.Standard }, HandoffBehavior.SnapshotAndReplace);
        }

        private static bool Near(Rect a, Rect b) =>
            Math.Abs(a.X - b.X) < 0.5 && Math.Abs(a.Y - b.Y) < 0.5 && Math.Abs(a.Width - b.Width) < 0.5 && Math.Abs(a.Height - b.Height) < 0.5;
    }
}

/// <summary>Atalho de teclado em capas de tecla (porte de shadcn-ui/kbd, MIT). Template em Theme/Controls.xaml.</summary>
public sealed class Kbd : Control
{
    public static readonly DependencyProperty ChordProperty = DependencyProperty.Register(
        nameof(Chord), typeof(string), typeof(Kbd), new PropertyMetadata(null, (d, e) => ((Kbd)d).Caps = KeyChord.Caps((string?)e.NewValue)));

    private static readonly DependencyPropertyKey CapsKey = DependencyProperty.RegisterReadOnly(
        nameof(Caps), typeof(IReadOnlyList<string>), typeof(Kbd), new PropertyMetadata(Array.Empty<string>()));

    public static readonly DependencyProperty CapsProperty = CapsKey.DependencyProperty;

    static Kbd()
    {
        FocusableProperty.OverrideMetadata(typeof(Kbd), new FrameworkPropertyMetadata(false));
    }

    public string? Chord
    {
        get => (string?)GetValue(ChordProperty);
        set => SetValue(ChordProperty, value);
    }

    public IReadOnlyList<string> Caps
    {
        get => (IReadOnlyList<string>)GetValue(CapsProperty);
        private set => SetValue(CapsKey, value);
    }
}

/// <summary>
/// Espaço reservado enquanto um dado carrega (porte de shadcn-ui/skeleton, MIT: <c>animate-pulse</c>). Pulsa só com
/// movimento ligado; com "Reduzir movimento", fica parado (spec §76).
/// </summary>
public sealed class Skeleton : Border
{
    public Skeleton()
    {
        CornerRadius = new CornerRadius(6);
        SetResourceReference(BackgroundProperty, "Brush.Bg.SurfaceHover");
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => BeginAnimation(OpacityProperty, null);
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Start();
            }
            else
            {
                BeginAnimation(OpacityProperty, null);
            }
        };
    }

    private void Start()
    {
        if (!Motion.Enabled || !IsVisible)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 0.7;
            return;
        }

        var pulse = new DoubleAnimation(1, 0.45, TimeSpan.FromSeconds(1)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = CubicBezierEase.EaseInOut };
        BeginAnimation(OpacityProperty, pulse);
    }
}

/// <summary>
/// Estado vazio (porte de shadcn-ui/empty, MIT): ícone, título, explicação e ações. Diz o que falta e o que fazer —
/// nunca uma área em branco (spec §65 "grandes áreas vazias"). Template em Theme/Controls.xaml.
/// </summary>
public sealed class EmptyState : ContentControl
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(Geometry), typeof(EmptyState), new PropertyMetadata(null));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata(null));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(EmptyState), new PropertyMetadata(null));

    static EmptyState()
    {
        FocusableProperty.OverrideMetadata(typeof(EmptyState), new FrameworkPropertyMetadata(false));
    }

    public Geometry? Glyph
    {
        get => (Geometry?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}

/// <summary>
/// Brilho que corre pelo texto enquanto algo está em andamento ("Aplicando…", "Procurando…"), porte de
/// beui/text-shimmer (MIT). Só liga com <c>TextShimmer.Active</c> verdadeiro e movimento ligado; desligado, o texto
/// volta à cor normal. Nunca decorativo nem permanente (spec §65, §75).
/// </summary>
public static class TextShimmer
{
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.RegisterAttached(
        "Active", typeof(bool), typeof(TextShimmer), new PropertyMetadata(false, OnActiveChanged));

    public static bool GetActive(DependencyObject d) => (bool)d.GetValue(ActiveProperty);

    public static void SetActive(DependencyObject d, bool value) => d.SetValue(ActiveProperty, value);

    // Máscara de opacidade em vez de trocar o Foreground: a cor continua vindo do estilo ou do binding.
    private static void OnActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        if (element.OpacityMask is LinearGradientBrush { RelativeTransform: TranslateTransform running })
        {
            running.BeginAnimation(TranslateTransform.XProperty, null);
        }

        if (!(bool)e.NewValue || !Motion.Enabled)
        {
            element.ClearValue(UIElement.OpacityMaskProperty);
            return;
        }

        var dim = Color.FromArgb(0x80, 0, 0, 0);
        var shift = new TranslateTransform(-1, 0);
        element.OpacityMask = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            RelativeTransform = shift,
            GradientStops =
            {
                new GradientStop(dim, 0),
                new GradientStop(dim, 0.35),
                new GradientStop(Colors.Black, 0.5),
                new GradientStop(dim, 0.65),
                new GradientStop(dim, 1),
            },
        };
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-1, 1, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
    }
}

public enum LoaderVariant
{
    /// <summary>Três pontos girando em volta de um anel (generative-loaders "orbit").</summary>
    Orbit,

    /// <summary>Quatro pontos que pulsam em sequência (generative-loaders "dot-pulse").</summary>
    DotPulse,
}

/// <summary>
/// Indicador de atividade em linha (porte de generative-loaders InlineLoader, MIT, variantes "orbit" and "dot-pulse").
/// Só existe enquanto há trabalho de verdade; com "Reduzir movimento" fica parado, ainda visível (spec §76).
/// </summary>
public sealed class InlineLoader : FrameworkElement
{
    public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
        nameof(Variant), typeof(LoaderVariant), typeof(InlineLoader), new FrameworkPropertyMetadata(LoaderVariant.Orbit, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(InlineLoader), new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ForegroundProperty = System.Windows.Documents.TextElement.ForegroundProperty.AddOwner(
        typeof(InlineLoader), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        "Phase", typeof(double), typeof(InlineLoader), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public InlineLoader()
    {
        Loaded += (_, _) => Restart();
        Unloaded += (_, _) => BeginAnimation(PhaseProperty, null);
        IsVisibleChanged += (_, _) => Restart();
    }

    public LoaderVariant Variant
    {
        get => (LoaderVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize) => new(Size, Size);

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        var phase = (double)GetValue(PhaseProperty);
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        if (Variant == LoaderVariant.DotPulse)
        {
            var dot = size * 0.18;
            for (var i = 0; i < 4; i++)
            {
                // il-dot-pulse: cada ponto cresce e clareia com 15% do ciclo de atraso.
                var local = ((phase - (i * 0.15)) % 1 + 1) % 1;
                var wave = Math.Sin(local * Math.PI);
                var brush = Foreground.Clone();
                brush.Opacity = 0.35 + (0.65 * wave);
                var x = (size * 0.03) + (dot / 2) + (i * ((size * 0.94) - dot) / 3);
                drawingContext.DrawEllipse(brush, null, new Point(x, c.Y), dot / 2 * (0.7 + (0.3 * wave)), dot / 2 * (0.7 + (0.3 * wave)));
            }

            return;
        }

        // il-orbit: anel em 30% e três pontos de 18% com opacidade 1, .58, .28, girando o conjunto.
        drawingContext.PushTransform(new RotateTransform(phase * 360, c.X, c.Y));
        drawingContext.DrawEllipse(null, new Pen(Foreground, Math.Max(1, size * 0.06)), c, size * 0.2, size * 0.2);
        var r = size * 0.09;
        var orbit = (size / 2) - r;
        double[] opacity = [1, 0.58, 0.28];
        for (var i = 0; i < 3; i++)
        {
            var angle = (-90 + (i * 120)) * Math.PI / 180;
            var brush = Foreground.Clone();
            brush.Opacity = opacity[i];
            drawingContext.DrawEllipse(brush, null, new Point(c.X + (orbit * Math.Cos(angle)), c.Y + (orbit * Math.Sin(angle))), r, r);
        }

        drawingContext.Pop();
    }

    private void Restart()
    {
        if (!IsVisible || !Motion.Enabled)
        {
            BeginAnimation(PhaseProperty, null);
            return;
        }

        BeginAnimation(PhaseProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1.2)) { RepeatBehavior = RepeatBehavior.Forever });
    }
}

/// <summary>Arco de progresso 0–1 (anel do <see cref="HoldButton"/> e contagem regressiva da confirmação).</summary>
public sealed class ArcProgress : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(ArcProgress), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(ArcProgress), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(ArcProgress), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(ArcProgress), new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush? Track
    {
        get => (Brush?)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var radius = (Math.Min(ActualWidth, ActualHeight) - Thickness) / 2;
        if (radius <= 0)
        {
            return;
        }

        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        if (Track is not null)
        {
            drawingContext.DrawEllipse(null, new Pen(Track, Thickness), c, radius, radius);
        }

        var p = Math.Clamp(Progress, 0, 1);
        if (p <= 0)
        {
            return;
        }

        var pen = new Pen(Stroke, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (p >= 0.9999)
        {
            drawingContext.DrawEllipse(null, pen, c, radius, radius);
            return;
        }

        var angle = (p * 360) - 90;
        var end = new Point(c.X + (radius * Math.Cos(angle * Math.PI / 180)), c.Y + (radius * Math.Sin(angle * Math.PI / 180)));
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(c.X, c.Y - radius), isFilled: false, isClosed: false);
            context.ArcTo(end, new System.Windows.Size(radius, radius), 0, p > 0.5, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }
}
