using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace OpenGG.Desktop.Controls;

/// <summary>
/// Moldura do card no Início. Sem altura imposta, o card fica com a altura natural, igual à da página de origem (nada
/// dentro dele é redimensionado). Com altura fixada menor, o resto rola; largo e baixo (<see cref="IsWide"/>), os
/// cards de dispositivo põem a foto ao lado do texto.
/// </summary>
public sealed class AdaptiveCardFrame : Decorator
{
    public static readonly DependencyProperty IsWideProperty = DependencyProperty.Register(nameof(IsWide), typeof(bool), typeof(AdaptiveCardFrame));

    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
        Focusable = false,
    };
    private readonly Grid _viewport = new();
    private readonly Border _outline = new() { IsHitTestVisible = false, CornerRadius = new CornerRadius(16) };
    private Border? _surface;

    public AdaptiveCardFrame()
    {
        _viewport.Children.Add(_scroll);
        _viewport.Children.Add(_outline);
        Child = _viewport;
        ClipToBounds = true;
    }

    public bool IsWide { get => (bool)GetValue(IsWideProperty); private set => SetValue(IsWideProperty, value); }

    public UIElement? Content
    {
        get => _scroll.Content as UIElement;
        set { _surface = null; _scroll.Content = value; }
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var width = double.IsFinite(constraint.Width) ? constraint.Width : 360;
        IsWide = double.IsFinite(constraint.Height) && width > constraint.Height * 1.5 && width >= 360;
        _viewport.Measure(new Size(width, constraint.Height));
        if (_surface is null && Content is { } content && Surface(content) is { } surface)
        {
            _surface = surface;
            foreach (var property in new[] { Border.BorderBrushProperty, Border.BorderThicknessProperty, Border.CornerRadiusProperty })
                _outline.SetBinding(property, new Binding(property.Name) { Source = surface });
        }
        return new Size(width, double.IsFinite(constraint.Height) ? constraint.Height : _scroll.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var size = base.ArrangeOverride(arrangeSize);
        // A borda pertence ao tamanho escolhido; o conteúdo maior rola dentro dela, sem cantos quadrados.
        var clip = new RectangleGeometry(new Rect(size), _outline.CornerRadius.TopLeft, _outline.CornerRadius.TopLeft);
        clip.Freeze();
        _viewport.Clip = clip;
        return size;
    }

    private static Border? Surface(DependencyObject element)
    {
        if (element is Border { Background: not null, CornerRadius.TopLeft: >= 12 } border) return border;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            if (Surface(VisualTreeHelper.GetChild(element, i)) is { } found) return found;
        return null;
    }
}
