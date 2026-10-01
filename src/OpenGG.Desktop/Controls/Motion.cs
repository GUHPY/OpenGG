using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AppMotion = OneRGB.Application.Presentation;

namespace OpenGG.Desktop.Controls;

/// <summary>
/// Easing cubic-bezier do design system (design/tokens/motion/motion.css) para animações WPF.
/// A matemática fica em OneRGB.Application.Presentation.CubicBezier (testada).
/// </summary>
public sealed class CubicBezierEase : EasingFunctionBase
{
    private AppMotion.CubicBezier _curve = AppMotion.CubicBezier.Standard;

    public CubicBezierEase()
    {
        EasingMode = EasingMode.EaseIn; // o bezier já descreve a curva inteira; EaseIn = sem inversão
    }

    public CubicBezierEase(AppMotion.CubicBezier curve)
        : this() => _curve = curve;

    /// <summary>
    /// Nome do preset no XAML: Standard, Emphasized, EaseOut, EaseInOut. O "--ease-spring" do CSS passa do alvo, o que a
    /// spec §75 proíbe; no WPF "Spring" vira Emphasized, e a mola de verdade é o <see cref="SpringEase"/> (sem ultrapassar).
    /// </summary>
    public string Preset
    {
        get => _curve == AppMotion.CubicBezier.Emphasized ? "Emphasized" : "Standard";
        set => _curve = value switch
        {
            "Emphasized" or "Spring" => AppMotion.CubicBezier.Emphasized,
            "EaseOut" => AppMotion.CubicBezier.EaseOut,
            "EaseInOut" => AppMotion.CubicBezier.EaseInOut,
            _ => AppMotion.CubicBezier.Standard,
        };
    }

    public static CubicBezierEase Standard { get; } = new(AppMotion.CubicBezier.Standard);

    public static CubicBezierEase Emphasized { get; } = new(AppMotion.CubicBezier.Emphasized);

    public static CubicBezierEase EaseInOut { get; } = new(AppMotion.CubicBezier.EaseInOut);

    protected override double EaseInCore(double normalizedTime) => _curve.Evaluate(normalizedTime);

    protected override Freezable CreateInstanceCore() => new CubicBezierEase(_curve);
}

/// <summary>
/// Presets de animação portados de design/clean/animations/motion-presets.ts (cardHover, staggerContainer,
/// staggerItem, pulseSync, modal). Respeitam "Reduzir movimento" (Configurações).
/// </summary>
public static class Motion
{
    // Escala do design system: toque 80, micro 120, componente 200, painel 280, página 360 ms.
    public static readonly TimeSpan Instant = TimeSpan.FromMilliseconds(80);
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan Standard = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(280);
    public static readonly TimeSpan Deliberate = TimeSpan.FromMilliseconds(360);

    private static bool _reducedSetting;

    static Motion()
    {
        // "Mostrar animações no Windows" desligado também reduz o movimento (spec §76), inclusive com o app aberto.
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
            {
                Apply(_reducedSetting);
            }
        };
    }

    /// <summary>Falso quando o usuário ativa "Reduzir movimento": animações viram mudanças instantâneas.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>Liga o movimento conforme a configuração do app e a preferência de animações do Windows.</summary>
    public static void Apply(bool reducedSetting)
    {
        _reducedSetting = reducedSetting;
        Enabled = !reducedSetting && SystemParameters.ClientAreaAnimation;
    }

    // ------------------------------------------------------------------ cardHover (y -2, scale 1.01)
    public static readonly DependencyProperty HoverLiftProperty = DependencyProperty.RegisterAttached(
        "HoverLift", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnHoverLiftChanged));

    public static bool GetHoverLift(DependencyObject d) => (bool)d.GetValue(HoverLiftProperty);

    public static void SetHoverLift(DependencyObject d, bool value) => d.SetValue(HoverLiftProperty, value);

    // ------------------------------------------------------------------ staggerContainer / staggerItem
    public static readonly DependencyProperty StaggerProperty = DependencyProperty.RegisterAttached(
        "Stagger", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnStaggerChanged));

    public static bool GetStagger(DependencyObject d) => (bool)d.GetValue(StaggerProperty);

    public static void SetStagger(DependencyObject d, bool value) => d.SetValue(StaggerProperty, value);

    // ------------------------------------------------------------------ entrada simples (fade + y 12→0)
    public static readonly DependencyProperty EnterProperty = DependencyProperty.RegisterAttached(
        "Enter", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnEnterChanged));

    public static bool GetEnter(DependencyObject d) => (bool)d.GetValue(EnterProperty);

    public static void SetEnter(DependencyObject d, bool value) => d.SetValue(EnterProperty, value);

    public static readonly DependencyProperty EnterDelayProperty = DependencyProperty.RegisterAttached(
        "EnterDelay", typeof(int), typeof(Motion), new PropertyMetadata(0));

    public static int GetEnterDelay(DependencyObject d) => (int)d.GetValue(EnterDelayProperty);

    public static void SetEnterDelay(DependencyObject d, int value) => d.SetValue(EnterDelayProperty, value);

    // Hidden panels retain their measured height, so switching tabs does not clamp the page scroll.
    public static readonly DependencyProperty PanelActiveProperty = DependencyProperty.RegisterAttached(
        "PanelActive", typeof(bool), typeof(Motion), new PropertyMetadata(true, OnPanelActiveChanged));
    public static bool GetPanelActive(DependencyObject d) => (bool)d.GetValue(PanelActiveProperty);
    public static void SetPanelActive(DependencyObject d, bool value) => d.SetValue(PanelActiveProperty, value);
    private static void OnPanelActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement panel) return;
        panel.Loaded -= PanelLoaded;
        panel.Loaded += PanelLoaded;
        SwitchPanel(panel);
    }
    private static void PanelLoaded(object sender, RoutedEventArgs e) => SwitchPanel((FrameworkElement)sender);
    private static async void SwitchPanel(FrameworkElement panel)
    {
        var active = GetPanelActive(panel);
        panel.IsHitTestVisible = active;
        var (offset, _) = EnsureTransforms(panel);
        if (!panel.IsLoaded || !Enabled)
        {
            panel.Visibility = active ? Visibility.Visible : Visibility.Hidden;
            panel.Opacity = active ? 1 : 0;
            offset.X = 0;
            return;
        }
        if (active)
        {
            if (panel.Visibility != Visibility.Visible) { panel.Opacity = 0; offset.X = 10; }
            panel.Visibility = Visibility.Visible;
            To(panel, UIElement.OpacityProperty, 1, Standard, CubicBezierEase.Emphasized);
            To(offset, TranslateTransform.XProperty, 0, Standard, CubicBezierEase.Emphasized);
        }
        else
        {
            To(panel, UIElement.OpacityProperty, 0, Fast);
            To(offset, TranslateTransform.XProperty, -8, Fast);
            await Task.Delay(Fast);
            if (!GetPanelActive(panel)) panel.Visibility = Visibility.Hidden;
        }
    }

    // ------------------------------------------------------------------ pulseSync (scale 1→1.25→1, 1.6 s)
    public static readonly DependencyProperty PulseProperty = DependencyProperty.RegisterAttached(
        "Pulse", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnPulseChanged));

    public static bool GetPulse(DependencyObject d) => (bool)d.GetValue(PulseProperty);

    public static void SetPulse(DependencyObject d, bool value) => d.SetValue(PulseProperty, value);

    // ------------------------------------------------------------------ giro contínuo (ícone "sincronizando")
    public static readonly DependencyProperty SpinProperty = DependencyProperty.RegisterAttached(
        "Spin", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnSpinChanged));

    public static bool GetSpin(DependencyObject d) => (bool)d.GetValue(SpinProperty);

    public static void SetSpin(DependencyObject d, bool value) => d.SetValue(SpinProperty, value);

    // ------------------------------------------------------------------ slider: valor mudado por código desliza até o destino
    public static readonly DependencyProperty SmoothValueProperty = DependencyProperty.RegisterAttached(
        "SmoothValue", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnSmoothValueChanged));

    public static bool GetSmoothValue(DependencyObject d) => (bool)d.GetValue(SmoothValueProperty);

    public static void SetSmoothValue(DependencyObject d, bool value) => d.SetValue(SmoothValueProperty, value);

    /// <summary>Anima uma propriedade double com a curva e duração dadas (ou pula direto se o movimento estiver reduzido).</summary>
    public static void To(Animatable target, DependencyProperty property, double value, TimeSpan duration, IEasingFunction? ease = null, TimeSpan? delay = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!Enabled)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, value);
            return;
        }

        var animation = new DoubleAnimation(value, new Duration(duration))
        {
            EasingFunction = ease ?? CubicBezierEase.Standard,
            BeginTime = delay ?? TimeSpan.Zero,
        };
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    public static void To(UIElement target, DependencyProperty property, double value, TimeSpan duration, IEasingFunction? ease = null, TimeSpan? delay = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!Enabled)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, value);
            return;
        }

        var animation = new DoubleAnimation(value, new Duration(duration))
        {
            EasingFunction = ease ?? CubicBezierEase.Standard,
            BeginTime = delay ?? TimeSpan.Zero,
        };
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>staggerItem: opacidade 0→1 e deslocamento y 12→0 (250 ms, curva "emphasized").</summary>
    public static void EnterFrom(UIElement element, TimeSpan delay, double fromY = 12)
    {
        ArgumentNullException.ThrowIfNull(element);
        var (translate, _) = EnsureTransforms(element);
        if (!Enabled)
        {
            element.Opacity = 1;
            translate.Y = 0;
            return;
        }

        element.Opacity = 0;
        translate.Y = fromY;
        To(element, UIElement.OpacityProperty, 1, Standard, CubicBezierEase.Emphasized, delay);
        To(translate, TranslateTransform.YProperty, 0, Standard, CubicBezierEase.Emphasized, delay);
    }

    /// <summary>Transição de página: sai com fade rápido, entra com fade + deslize (motion "modal"/"standard").</summary>
    public static void PageIn(FrameworkElement page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var (translate, scale) = EnsureTransforms(page);
        if (!Enabled)
        {
            page.Opacity = 1;
            translate.Y = 0;
            scale.ScaleX = scale.ScaleY = 1;
            return;
        }

        page.Opacity = 0;
        translate.Y = 8;
        scale.ScaleX = scale.ScaleY = 0.995;
        To(page, UIElement.OpacityProperty, 1, Slow, CubicBezierEase.Emphasized);
        To(translate, TranslateTransform.YProperty, 0, Deliberate, CubicBezierEase.Emphasized);
        To(scale, ScaleTransform.ScaleXProperty, 1, Deliberate, CubicBezierEase.Emphasized);
        To(scale, ScaleTransform.ScaleYProperty, 1, Deliberate, CubicBezierEase.Emphasized);
    }

    /// <summary>Garante TranslateTransform + ScaleTransform (nessa ordem) no RenderTransform do elemento.</summary>
    public static (TranslateTransform Translate, ScaleTransform Scale) EnsureTransforms(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.RenderTransform is TransformGroup { Children.Count: 2 } group
            && group.Children[0] is ScaleTransform s && !s.IsFrozen
            && group.Children[1] is TranslateTransform t && !t.IsFrozen)
        {
            return (t, s);
        }

        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform();
        element.RenderTransform = new TransformGroup { Children = { scale, translate } };
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        return (translate, scale);
    }

    private static void OnSmoothValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Slider slider)
        {
            return;
        }

        slider.ValueChanged -= Glide;
        if ((bool)e.NewValue)
        {
            slider.ValueChanged += Glide;
        }
    }

    /// <summary>
    /// FLIP: o layout já põe polegar e preenchimento no valor novo; um deslocamento (e uma escala no preenchimento)
    /// compensa a diferença e volta a zero em 200 ms. O valor e o binding nunca são animados. Arrasto e teclado ficam
    /// instantâneos.
    /// </summary>
    private static void Glide(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var slider = (Slider)sender;
        var range = slider.Maximum - slider.Minimum;
        if (!Enabled || !slider.IsLoaded || range <= 0 || slider.IsKeyboardFocused
            || slider.Template?.FindName("PART_Track", slider) is not Track { Thumb: { IsDragging: false } thumb, DecreaseRepeatButton: { } fill } track)
        {
            return;
        }

        var vertical = slider.Orientation == Orientation.Vertical;
        var length = vertical ? track.ActualHeight - thumb.ActualHeight : track.ActualWidth - thumb.ActualWidth;
        var shift = (Math.Clamp(e.NewValue, slider.Minimum, slider.Maximum) - Math.Clamp(e.OldValue, slider.Minimum, slider.Maximum)) / range * length;
        if (length <= 0 || Math.Abs(shift) < 0.5)
        {
            return;
        }

        // O preenchimento fica entre a ponta mínima e o polegar: o polegar se afasta do lado onde ele está.
        var thumbAt = thumb.TranslatePoint(default, track);
        var fillAt = fill.TranslatePoint(default, track);
        var away = vertical ? fillAt.Y <= thumbAt.Y : fillAt.X <= thumbAt.X;
        var move = away ? shift : -shift;

        var offset = thumb.RenderTransform as TranslateTransform;
        if (offset is null || offset.IsFrozen)
        {
            offset = new TranslateTransform();
            thumb.RenderTransform = offset;
        }

        var axis = vertical ? TranslateTransform.YProperty : TranslateTransform.XProperty;
        offset.BeginAnimation(axis, new DoubleAnimation((double)offset.GetValue(axis) - move, 0, new Duration(Standard)) { EasingFunction = CubicBezierEase.Emphasized });

        var before = vertical ? fill.ActualHeight : fill.ActualWidth;
        var after = before + shift;
        if (after < 1)
        {
            return;
        }

        var scale = fill.RenderTransform as ScaleTransform;
        if (scale is null || scale.IsFrozen)
        {
            scale = new ScaleTransform();
            fill.RenderTransform = scale;
        }

        fill.RenderTransformOrigin = vertical ? new Point(0.5, away ? 0 : 1) : new Point(away ? 0 : 1, 0.5);
        var scaleAxis = vertical ? ScaleTransform.ScaleYProperty : ScaleTransform.ScaleXProperty;
        var from = before * (double)scale.GetValue(scaleAxis) / after;
        scale.BeginAnimation(scaleAxis, new DoubleAnimation(from, 1, new Duration(Standard)) { EasingFunction = CubicBezierEase.Emphasized });
    }

    private static void OnHoverLiftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            element.MouseEnter += Lift;
            element.MouseLeave += Drop;
            element.PreviewMouseLeftButtonDown += Tap;
            element.PreviewMouseLeftButtonUp += Lift;
        }
        else
        {
            element.MouseEnter -= Lift;
            element.MouseLeave -= Drop;
            element.PreviewMouseLeftButtonDown -= Tap;
            element.PreviewMouseLeftButtonUp -= Lift;
        }

        static void Lift(object sender, EventArgs args) => HoverTo((UIElement)sender, -2, 1.01, Fast);
        static void Drop(object sender, EventArgs args) => HoverTo((UIElement)sender, 0, 1, Fast);
        static void Tap(object sender, EventArgs args) => HoverTo((UIElement)sender, 0, 0.99, Instant);
    }

    private static void HoverTo(UIElement element, double y, double s, TimeSpan duration)
    {
        var (translate, scale) = EnsureTransforms(element);
        To(translate, TranslateTransform.YProperty, y, duration);
        To(scale, ScaleTransform.ScaleXProperty, s, duration);
        To(scale, ScaleTransform.ScaleYProperty, s, duration);
    }

    private static void OnStaggerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl items || !(bool)e.NewValue)
        {
            return;
        }

        items.ItemContainerGenerator.StatusChanged += (_, _) =>
        {
            if (items.ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated)
            {
                return;
            }

            // staggerChildren 0.04 s, delayChildren 0.02 s
            for (var i = 0; i < items.Items.Count; i++)
            {
                if (items.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement container && container.Tag is not "entered")
                {
                    container.Tag = "entered";
                    EnterFrom(container, TimeSpan.FromMilliseconds(20 + (Math.Min(i, 20) * 40)));
                }
            }
        };
    }

    private static void OnEnterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && (bool)e.NewValue)
        {
            element.Opacity = Enabled ? 0 : 1;
            element.Loaded += (_, _) => EnterFrom(element, TimeSpan.FromMilliseconds(GetEnterDelay(element)));
        }
    }

    private static void OnPulseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        var (_, scale) = EnsureTransforms(element);
        if (!(bool)e.NewValue || !Enabled)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            element.BeginAnimation(UIElement.OpacityProperty, null);
            return;
        }

        var grow = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1.6), RepeatBehavior = RepeatBehavior.Forever };
        grow.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0)));
        grow.KeyFrames.Add(new EasingDoubleKeyFrame(1.25, KeyTime.FromPercent(0.5), CubicBezierEase.EaseInOut));
        grow.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), CubicBezierEase.EaseInOut));
        var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1.6), RepeatBehavior = RepeatBehavior.Forever };
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(0.8, KeyTime.FromPercent(0)));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0.5), CubicBezierEase.EaseInOut));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(0.8, KeyTime.FromPercent(1), CubicBezierEase.EaseInOut));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static void OnSpinChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.RenderTransformOrigin = new Point(0.5, 0.5);
        if (element.RenderTransform is not RotateTransform rotate || rotate.IsFrozen)
        {
            rotate = new RotateTransform();
            element.RenderTransform = rotate;
        }

        if ((bool)e.NewValue && Enabled)
        {
            rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            rotate.Angle = 0;
        }
    }
}
