using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OneRGB.Application.Presentation;

namespace OpenGG.Desktop.Controls;

/// <summary>
/// Número que desliza até o valor novo (porte de magicui/number-ticker, MIT). A mola é criticamente amortecida e
/// curta: mostra a mudança sem atrasar a leitura (spec §75). Algarismos tabulares para o texto não tremer.
/// </summary>
public sealed class AnimatedNumber : TextBlock
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(AnimatedNumber), new PropertyMetadata(double.NaN, OnValueChanged));

    public static readonly DependencyProperty DecimalsProperty = DependencyProperty.Register(
        nameof(Decimals), typeof(int), typeof(AnimatedNumber), new PropertyMetadata(0, (d, _) => ((AnimatedNumber)d).Render()));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(AnimatedNumber), new PropertyMetadata(null, (d, _) => ((AnimatedNumber)d).Render()));

    private static readonly DependencyProperty ShownProperty = DependencyProperty.Register(
        "Shown", typeof(double), typeof(AnimatedNumber), new PropertyMetadata(0.0, (d, _) => ((AnimatedNumber)d).Render()));

    public AnimatedNumber()
    {
        System.Windows.Documents.Typography.SetNumeralAlignment(this, FontNumeralAlignment.Tabular);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Decimals
    {
        get => (int)GetValue(DecimalsProperty);
        set => SetValue(DecimalsProperty, value);
    }

    public string? Unit
    {
        get => (string?)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var number = (AnimatedNumber)d;
        var to = (double)e.NewValue;
        var from = (double)e.OldValue;
        if (!double.IsFinite(to) || !double.IsFinite(from) || !Motion.Enabled || !number.IsLoaded)
        {
            number.BeginAnimation(ShownProperty, null);
            number.SetValue(ShownProperty, double.IsFinite(to) ? to : 0);
            number.Render();
            return;
        }

        number.BeginAnimation(ShownProperty, new DoubleAnimation(to, new Duration(SpringEase.DurationOf(SpringConfig.Quick))) { EasingFunction = SpringEase.Quick }, HandoffBehavior.SnapshotAndReplace);
    }

    private void Render()
    {
        if (!double.IsFinite(Value))
        {
            Text = "—";
            return;
        }

        var shown = (double)GetValue(ShownProperty);
        var text = shown.ToString("F" + Math.Clamp(Decimals, 0, 6).ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture);
        Text = string.IsNullOrEmpty(Unit) ? text : Unit is "%" ? text + Unit : $"{text} {Unit}";
    }
}

/// <summary>
/// Campo numérico com arrasto no rótulo (porte de microkit/scrub-number-field, MIT; spec §73): arrastar muda em
/// passos (Shift ×10, Alt ÷10), clicar sem arrastar abre a digitação, setas ajustam, Enter confirma, Esc desfaz, e o
/// valor exato fica sempre visível com a unidade. Bater no limite dá um "empurrão" curto. Template em Theme/Controls.xaml.
/// </summary>
[TemplatePart(Name = "PART_Scrub", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_Text", Type = typeof(TextBox))]
public sealed class ScrubNumberBox : Control
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ScrubNumberBox), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ScrubNumberBox)d).ShowValue()));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(ScrubNumberBox), new PropertyMetadata(0.0));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(ScrubNumberBox), new PropertyMetadata(100.0));

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(ScrubNumberBox), new PropertyMetadata(1.0, (d, _) => ((ScrubNumberBox)d).ShowValue()));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(ScrubNumberBox), new PropertyMetadata(null));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(ScrubNumberBox), new PropertyMetadata(null));

    /// <summary>Com falso, o valor só muda ao soltar o arrasto (útil quando cada passo escreveria no hardware).</summary>
    public static readonly DependencyProperty LiveProperty = DependencyProperty.Register(
        nameof(Live), typeof(bool), typeof(ScrubNumberBox), new PropertyMetadata(true));

    private static readonly DependencyPropertyKey ScrubbingKey = DependencyProperty.RegisterReadOnly(
        nameof(Scrubbing), typeof(bool), typeof(ScrubNumberBox), new PropertyMetadata(false));

    public static readonly DependencyProperty ScrubbingProperty = ScrubbingKey.DependencyProperty;

    public static readonly RoutedEvent CommittedEvent = EventManager.RegisterRoutedEvent(
        nameof(Committed), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ScrubNumberBox));

    private FrameworkElement? _scrub;
    private TextBox? _text;
    private double _dragOrigin;
    private double _dragBase;
    private double _dragValue;
    private double _dragStep = 1;
    private bool _dragShift;
    private bool _dragAlt;
    private bool _dragMoved;
    private bool _reverting;

    public event RoutedEventHandler Committed
    {
        add => AddHandler(CommittedEvent, value);
        remove => RemoveHandler(CommittedEvent, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public string? Unit
    {
        get => (string?)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool Live
    {
        get => (bool)GetValue(LiveProperty);
        set => SetValue(LiveProperty, value);
    }

    public bool Scrubbing
    {
        get => (bool)GetValue(ScrubbingProperty);
        private set => SetValue(ScrubbingKey, value);
    }

    private NumericRange Range => new(Minimum, Maximum, Step);

    public override void OnApplyTemplate()
    {
        if (_scrub is not null)
        {
            _scrub.PreviewMouseLeftButtonDown -= OnScrubDown;
            _scrub.PreviewMouseMove -= OnScrubMove;
            _scrub.PreviewMouseLeftButtonUp -= OnScrubUp;
            _scrub.LostMouseCapture -= OnScrubLost;
        }

        if (_text is not null)
        {
            _text.PreviewKeyDown -= OnTextKey;
            _text.LostKeyboardFocus -= OnTextLostFocus;
        }

        base.OnApplyTemplate();
        _scrub = GetTemplateChild("PART_Scrub") as FrameworkElement;
        _text = GetTemplateChild("PART_Text") as TextBox;
        if (_scrub is not null)
        {
            _scrub.Cursor = Cursors.SizeWE;
            _scrub.PreviewMouseLeftButtonDown += OnScrubDown;
            _scrub.PreviewMouseMove += OnScrubMove;
            _scrub.PreviewMouseLeftButtonUp += OnScrubUp;
            _scrub.LostMouseCapture += OnScrubLost;
        }

        if (_text is not null)
        {
            _text.PreviewKeyDown += OnTextKey;
            _text.LostKeyboardFocus += OnTextLostFocus;
        }

        ShowValue();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!IsKeyboardFocusWithin || !IsEnabled)
        {
            base.OnMouseWheel(e);
            return;
        }

        Settle(Range.Nudge(Value, Math.Sign(e.Delta), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)), Value + (Math.Sign(e.Delta) * Step));
        e.Handled = true;
    }

    private static bool Shift => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

    private static bool Alt => Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

    private void OnScrubDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled || _scrub is null || (_text?.IsKeyboardFocusWithin ?? false))
        {
            return;
        }

        e.Handled = true;
        _scrub.CaptureMouse();
        _dragOrigin = e.GetPosition(this).X;
        _dragBase = _dragValue = Value;
        (_dragShift, _dragAlt) = (Shift, Alt);
        _dragStep = Range.StepFor(_dragShift, _dragAlt);
        _dragMoved = false;
    }

    private void OnScrubMove(object sender, MouseEventArgs e)
    {
        if (_scrub is null || !_scrub.IsMouseCaptured)
        {
            return;
        }

        // Apertar Shift no meio do arrasto recomeça a conta a partir daqui, em vez de multiplicar o já percorrido.
        var x = e.GetPosition(this).X;
        if (Shift != _dragShift || Alt != _dragAlt)
        {
            (_dragShift, _dragAlt) = (Shift, Alt);
            _dragStep = Range.StepFor(_dragShift, _dragAlt);
            _dragOrigin = x;
            _dragBase = _dragValue;
            _dragMoved = true;
        }

        var travel = x - _dragOrigin;
        if (!_dragMoved && Math.Abs(travel) < NumericRange.DragThreshold)
        {
            return;
        }

        _dragMoved = true;
        Scrubbing = true;
        // Passo fixo no arrasto inteiro: um Step que acompanha o valor (DPI por faixas) faria o valor pular no meio.
        var step = _dragStep;
        var wanted = _dragBase + (Math.Round(travel / NumericRange.PixelsPerStep) * step);
        _dragValue = Range.Snap(wanted, step);
        if (Range.Bound(wanted) != 0)
        {
            Nudge(Range.Bound(wanted));
        }

        if (Live)
        {
            Value = _dragValue;
        }
        else
        {
            ShowText(_dragValue, _dragAlt);
        }
    }

    private void OnScrubUp(object sender, MouseButtonEventArgs e)
    {
        if (_scrub is null || !_scrub.IsMouseCaptured)
        {
            return;
        }

        _scrub.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnScrubLost(object sender, MouseEventArgs e)
    {
        var moved = _dragMoved;
        Scrubbing = false;
        _dragMoved = false;
        if (moved)
        {
            Value = _dragValue;
            RaiseEvent(new RoutedEventArgs(CommittedEvent, this));
            return;
        }

        // Clique sem arrastar: vira campo de texto, como no original.
        if (_text is not null)
        {
            _text.Focus();
            _text.SelectAll();
        }
    }

    private void OnTextKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Commit();
                Keyboard.ClearFocus();
                e.Handled = true;
                break;
            case Key.Escape:
                // O Esc descarta o rascunho; a perda de foco que vem depois não pode gravá-lo.
                _reverting = true;
                ShowValue();
                Keyboard.ClearFocus();
                e.Handled = true;
                break;
            case Key.Up or Key.Down:
                var direction = e.Key == Key.Up ? 1 : -1;
                Settle(Range.Nudge(Value, direction, Shift, Alt), Value + (direction * Range.StepFor(Shift, Alt)));
                e.Handled = true;
                break;
        }
    }

    private void OnTextLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_reverting)
        {
            Commit();
        }

        _reverting = false;
    }

    private void Commit()
    {
        if (_text is null)
        {
            return;
        }

        if (NumericRange.TryParse(_text.Text, out var typed))
        {
            Settle(Range.Snap(typed), typed);
        }
        else
        {
            ShowValue();
        }
    }

    private void Settle(double value, double wanted)
    {
        if (Range.Bound(wanted) != 0)
        {
            Nudge(Range.Bound(wanted));
        }

        var changed = Math.Abs(value - Value) > 1e-12;
        Value = value;
        ShowValue();
        if (changed)
        {
            RaiseEvent(new RoutedEventArgs(CommittedEvent, this));
        }
    }

    private void ShowValue() => ShowText(Value, alt: false);

    private void ShowText(double value, bool alt)
    {
        if (_text is not null && !_text.IsKeyboardFocusWithin)
        {
            _text.Text = Range.Format(value, CultureInfo.CurrentCulture, alt);
        }
    }

    /// <summary>"Empurrão" de 5 px para o lado do limite (260 ms), sem repetir enquanto um está rodando.</summary>
    private void Nudge(int direction)
    {
        if (!Motion.Enabled || (RenderTransform is TranslateTransform { HasAnimatedProperties: true }))
        {
            return;
        }

        var shift = new TranslateTransform();
        RenderTransform = shift;
        var shake = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(260) };
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(5 * direction, KeyTime.FromPercent(0.4), CubicBezierEase.Standard));
        shake.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1), CubicBezierEase.Standard));
        shake.Completed += (_, _) => RenderTransform = Transform.Identity;
        shift.BeginAnimation(TranslateTransform.XProperty, shake);
    }
}

/// <summary>
/// Régua que corre sob uma agulha fixa (porte de beui/range-slider-ruler, MIT): arrastar move a escala, soltar
/// assenta no passo mais próximo com mola sem ultrapassagem; setas, Page Up/Down, Home/End e a roda também ajustam.
/// O valor exato fica em cima, com a unidade (spec §73).
/// </summary>
public sealed class RulerSlider : RangeBase
{
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(RulerSlider), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MajorEveryProperty = DependencyProperty.Register(
        nameof(MajorEvery), typeof(int), typeof(RulerSlider), new FrameworkPropertyMetadata(5, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(RulerSlider), new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(RulerSlider), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty OffsetProperty = DependencyProperty.Register(
        "Offset", typeof(double), typeof(RulerSlider), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnOffsetChanged));

    private bool _dragging;
    private bool _settling;
    private double _dragStartX;
    private double _dragStartOffset;

    static RulerSlider()
    {
        FocusableProperty.OverrideMetadata(typeof(RulerSlider), new FrameworkPropertyMetadata(true));
        MaximumProperty.OverrideMetadata(typeof(RulerSlider), new FrameworkPropertyMetadata(100.0));
    }

    public RulerSlider()
    {
        Height = 92;
        FocusVisualStyle = null;
        Cursor = Cursors.SizeWE;
        SetResourceReference(ForegroundProperty, "Brush.Text.Primary");
    }

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public int MajorEvery
    {
        get => (int)GetValue(MajorEveryProperty);
        set => SetValue(MajorEveryProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public string? Unit
    {
        get => (string?)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        if (!_dragging && !_settling)
        {
            BeginAnimation(OffsetProperty, null);
            SetValue(OffsetProperty, Ruler.OffsetOf(newValue, Minimum, Step, Gap));
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);
        Focus();
        BeginAnimation(OffsetProperty, null);
        _settling = false;
        _dragging = CaptureMouse();
        _dragStartX = e.GetPosition(this).X;
        _dragStartOffset = (double)GetValue(OffsetProperty);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        if (!_dragging)
        {
            return;
        }

        var max = Ruler.OffsetOf(Maximum, Minimum, Step, Gap);
        var offset = Math.Clamp(_dragStartOffset - (e.GetPosition(this).X - _dragStartX), 0, max);
        SetValue(OffsetProperty, offset);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonUp(e);
        if (_dragging)
        {
            ReleaseMouseCapture();
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        SnapToTick();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var range = new NumericRange(Minimum, Maximum, Step);
        double? next = e.Key switch
        {
            Key.Left or Key.Down => range.Nudge(Value, -1, shift, alt: false),
            Key.Right or Key.Up => range.Nudge(Value, 1, shift, alt: false),
            Key.PageDown => range.Nudge(Value, -1, shift: true, alt: false),
            Key.PageUp => range.Nudge(Value, 1, shift: true, alt: false),
            Key.Home => Minimum,
            Key.End => Maximum,
            _ => null,
        };
        if (next is { } value)
        {
            Value = value;
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (!IsKeyboardFocusWithin)
        {
            base.OnMouseWheel(e);
            return;
        }

        Value = new NumericRange(Minimum, Maximum, Step).Nudge(Value, Math.Sign(e.Delta), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), alt: false);
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var width = ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var provider = CultureInfo.CurrentCulture;
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var range = new NumericRange(Minimum, Maximum, Step);
        var offset = (double)GetValue(OffsetProperty);
        var shownValue = _dragging || _settling ? Ruler.ValueAt(offset, Minimum, Maximum, Step, Gap) : Value;

        // Leitura: valor grande + unidade menor.
        var readout = new FormattedText(range.Format(shownValue, provider), provider, FlowDirection.LeftToRight, typeface, 28, Foreground, dpi);
        var unit = string.IsNullOrEmpty(Unit) ? null
            : new FormattedText(Unit, provider, FlowDirection.LeftToRight, new Typeface(FontFamily.Source), 13, Res("Brush.Text.Muted"), dpi);
        var total = readout.Width + (unit is null ? 0 : unit.Width + 4);
        drawingContext.DrawText(readout, new Point((width - total) / 2, 0));
        if (unit is not null)
        {
            drawingContext.DrawText(unit, new Point(((width - total) / 2) + readout.Width + 4, readout.Baseline - unit.Baseline));
        }

        // Escala: some nas bordas (máscara, não degradê com a cor de fundo — funciona sobre qualquer superfície).
        var top = 44.0;
        var center = width / 2;
        drawingContext.PushOpacityMask(new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            GradientStops = { new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.Black, 0.18), new GradientStop(Colors.Black, 0.82), new GradientStop(Colors.Transparent, 1) },
        });
        var minor = new Pen(Res("Brush.Text.Muted"), 1);
        var major = new Pen(Res("Brush.Text.Secondary"), 1);
        var label = new Typeface(FontFamily.Source);
        foreach (var tick in Ruler.Ticks(Minimum, Maximum, Step, MajorEvery, Gap))
        {
            var x = center + tick.Offset - offset;
            if (x < -Gap || x > width + Gap)
            {
                continue;
            }

            var height = tick.Major ? 28 : 14;
            drawingContext.DrawLine(tick.Major ? major : minor, new Point(Math.Round(x) + 0.5, top + 28 - height), new Point(Math.Round(x) + 0.5, top + 28));
            if (tick.Major)
            {
                var text = new FormattedText(tick.Value.ToString("0.######", provider), provider, FlowDirection.LeftToRight, label, 10, Res("Brush.Text.Muted"), dpi);
                drawingContext.DrawText(text, new Point(x - (text.Width / 2), top + 32));
            }
        }

        drawingContext.Pop();

        // Agulha: a cabeça de leitura sob a qual a escala se move.
        drawingContext.DrawRoundedRectangle(IsKeyboardFocused ? Res("Brush.Accent") : Foreground, null, new Rect(center - 1.5, top - 6, 3, 36), 1.5, 1.5);
    }

    private static void OnOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ruler = (RulerSlider)d;
        if (ruler._dragging)
        {
            ruler.Value = Ruler.ValueAt((double)e.NewValue, ruler.Minimum, ruler.Maximum, ruler.Step, ruler.Gap);
        }
    }

    private void SnapToTick()
    {
        var target = Ruler.ValueAt((double)GetValue(OffsetProperty), Minimum, Maximum, Step, Gap);
        var snapped = Ruler.OffsetOf(target, Minimum, Step, Gap);
        Value = target;
        if (!Motion.Enabled)
        {
            SetValue(OffsetProperty, snapped);
            return;
        }

        _settling = true;
        var settle = new DoubleAnimation(snapped, new Duration(SpringEase.DurationOf(SpringConfig.Quick))) { EasingFunction = SpringEase.Quick };
        settle.Completed += (_, _) =>
        {
            _settling = false;
            BeginAnimation(OffsetProperty, null);
            SetValue(OffsetProperty, Ruler.OffsetOf(Value, Minimum, Step, Gap));
        };
        BeginAnimation(OffsetProperty, settle, HandoffBehavior.SnapshotAndReplace);
    }

    private Brush Res(string key) => TryFindResource(key) as Brush ?? Foreground;
}

/// <summary>
/// Segurar para confirmar (adaptado de spectrum-ui/hold-to-confirm, Apache-2.0): segurar o botão (mouse, Espaço ou Enter)
/// enche o anel; soltar antes volta o anel e nada acontece; completar executa o comando uma vez, mostra o check e o
/// rótulo de confirmado e volta ao normal. Para ações destrutivas ou de risco (spec §74). Template em Theme/Controls.xaml.
/// </summary>
public sealed class HoldButton : Control, ICommandSource
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(HoldButton), new PropertyMetadata(null));

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter), typeof(object), typeof(HoldButton), new PropertyMetadata(null));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(HoldButton), new PropertyMetadata("Hold to confirm"));

    public static readonly DependencyProperty ConfirmedLabelProperty = DependencyProperty.Register(
        nameof(ConfirmedLabel), typeof(string), typeof(HoldButton), new PropertyMetadata("Confirmado"));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(Geometry), typeof(HoldButton), new PropertyMetadata(null));

    public static readonly DependencyProperty HoldDurationProperty = DependencyProperty.Register(
        nameof(HoldDuration), typeof(TimeSpan), typeof(HoldButton), new PropertyMetadata(TimeSpan.FromMilliseconds(1200)));

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(HoldButton), new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey HoldingKey = DependencyProperty.RegisterReadOnly(
        nameof(Holding), typeof(bool), typeof(HoldButton), new PropertyMetadata(false));

    public static readonly DependencyProperty HoldingProperty = HoldingKey.DependencyProperty;

    private static readonly DependencyPropertyKey ConfirmedKey = DependencyProperty.RegisterReadOnly(
        nameof(Confirmed), typeof(bool), typeof(HoldButton), new PropertyMetadata(false));

    public static readonly DependencyProperty ConfirmedProperty = ConfirmedKey.DependencyProperty;

    public static readonly RoutedEvent ConfirmedEvent = EventManager.RegisterRoutedEvent(
        "HoldConfirmed", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(HoldButton));

    private readonly HashSet<string> _sources = [];
    private bool _fired;

    static HoldButton()
    {
        FocusableProperty.OverrideMetadata(typeof(HoldButton), new FrameworkPropertyMetadata(true));
    }

    public event RoutedEventHandler HoldConfirmed
    {
        add => AddHandler(ConfirmedEvent, value);
        remove => RemoveHandler(ConfirmedEvent, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public IInputElement? CommandTarget => null;

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string ConfirmedLabel
    {
        get => (string)GetValue(ConfirmedLabelProperty);
        set => SetValue(ConfirmedLabelProperty, value);
    }

    public Geometry? Glyph
    {
        get => (Geometry?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public TimeSpan HoldDuration
    {
        get => (TimeSpan)GetValue(HoldDurationProperty);
        set => SetValue(HoldDurationProperty, value);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public bool Holding
    {
        get => (bool)GetValue(HoldingProperty);
        private set => SetValue(HoldingKey, value);
    }

    public bool Confirmed
    {
        get => (bool)GetValue(ConfirmedProperty);
        private set => SetValue(ConfirmedKey, value);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);
        if (CaptureMouse())
        {
            Focus();
            Begin("pointer");
        }

        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
        End("pointer");
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        End("pointer");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Key is Key.Space or Key.Enter)
        {
            if (!e.IsRepeat)
            {
                Begin("keyboard");
            }

            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Key is Key.Space or Key.Enter)
        {
            End("keyboard");
            e.Handled = true;
            return;
        }

        base.OnKeyUp(e);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        End("keyboard");
    }

    private void Begin(string source)
    {
        if (!IsEnabled || Confirmed || Command?.CanExecute(CommandParameter) == false)
        {
            return;
        }

        // Mouse e teclado juntos não recomeçam nem cancelam o enchimento enquanto um deles segue apertado.
        var first = _sources.Count == 0;
        _sources.Add(source);
        if (!first)
        {
            return;
        }

        Holding = true;
        _fired = false;
        var remaining = TimeSpan.FromMilliseconds(HoldDuration.TotalMilliseconds * (1 - Math.Clamp(Progress, 0, 1)));
        var fill = new DoubleAnimation(1, new Duration(remaining));
        fill.Completed += (_, _) =>
        {
            if (Holding && !_fired)
            {
                Complete();
            }
        };
        BeginAnimation(ProgressProperty, fill, HandoffBehavior.SnapshotAndReplace);
    }

    private void End(string source)
    {
        if (!_sources.Remove(source) || _sources.Count > 0)
        {
            return;
        }

        Holding = false;
        if (Confirmed || _fired)
        {
            return;
        }

        // Soltou antes: o anel volta e nada acontece.
        var back = new DoubleAnimation(0, new Duration(Motion.Enabled ? TimeSpan.FromMilliseconds(300) : TimeSpan.Zero)) { EasingFunction = CubicBezierEase.Standard };
        BeginAnimation(ProgressProperty, back, HandoffBehavior.SnapshotAndReplace);
    }

    private void Complete()
    {
        _fired = true;
        _sources.Clear();
        Holding = false;
        Confirmed = true;
        if (Command?.CanExecute(CommandParameter) != false)
        {
            Command?.Execute(CommandParameter);
        }

        RaiseEvent(new RoutedEventArgs(ConfirmedEvent, this));
        var reset = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        reset.Tick += (_, _) =>
        {
            reset.Stop();
            Confirmed = false;
            BeginAnimation(ProgressProperty, new DoubleAnimation(0, new Duration(Motion.Enabled ? TimeSpan.FromMilliseconds(300) : TimeSpan.Zero)), HandoffBehavior.SnapshotAndReplace);
        };
        reset.Start();
    }
}
