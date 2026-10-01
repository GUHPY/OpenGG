using System.Windows;
using System.Windows.Controls;

namespace OpenGG.Desktop.Controls;

/// <summary>
/// Grade que acompanha a largura: quantas colunas de <see cref="MinItemWidth"/> couberem, todas da mesma largura, sem
/// sobra torta à direita. A altura de cada linha é a do item mais alto dela. <c>ctl:ResponsiveGrid.Span</c> faz um
/// item ocupar mais colunas (limitado às que existem).
/// </summary>
public sealed class ResponsiveGrid : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(ResponsiveGrid), new FrameworkPropertyMetadata(260.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(ResponsiveGrid), new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(ResponsiveGrid), new FrameworkPropertyMetadata(12, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty BalanceRowsProperty = DependencyProperty.Register(
        nameof(BalanceRows), typeof(bool), typeof(ResponsiveGrid), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Distribui cards de uma coluna em linhas equilibradas (quatro cards viram 2 × 2, nunca 3 + 1).</summary>
    public bool BalanceRows
    {
        get => (bool)GetValue(BalanceRowsProperty);
        set => SetValue(BalanceRowsProperty, value);
    }

    public static readonly DependencyProperty SpanProperty = DependencyProperty.RegisterAttached(
        "Span", typeof(int), typeof(ResponsiveGrid), new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    private readonly List<(UIElement Child, int Row, int Column, int Span)> _layout = [];
    private readonly List<double> _rowHeights = [];
    private double _columnWidth;

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public static int GetSpan(DependencyObject element) => (int)(element ?? throw new ArgumentNullException(nameof(element))).GetValue(SpanProperty);

    public static void SetSpan(DependencyObject element, int value) => (element ?? throw new ArgumentNullException(nameof(element))).SetValue(SpanProperty, value);

    /// <summary>Colunas que cabem em <paramref name="width"/> (pelo menos uma).</summary>
    public static int ColumnsFor(double width, double minItemWidth, double gap, int maxColumns) =>
        Math.Clamp((int)Math.Floor((width + gap) / (Math.Max(1, minItemWidth) + gap)), 1, Math.Max(1, maxColumns));

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? (MinItemWidth * 3) + (Gap * 2) : availableSize.Width;
        var columns = ColumnsFor(width, MinItemWidth, Gap, MaxColumns);
        if (BalanceRows)
        {
            var count = InternalChildren.Cast<UIElement>().Count(c => c.Visibility != Visibility.Collapsed);
            if (count > 0) columns = (int)Math.Ceiling(count / Math.Ceiling((double)count / columns));
            for (var candidate = columns; candidate >= 2; candidate--)
            {
                if (count % candidate != 0) continue;
                columns = candidate;
                break;
            }
        }
        _columnWidth = Math.Max(0, (width - (Gap * (columns - 1))) / columns);
        _layout.Clear();
        _rowHeights.Clear();

        int row = 0, column = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            var span = Math.Clamp(GetSpan(child), 1, columns);
            if (column + span > columns)
            {
                row++;
                column = 0;
            }

            child.Measure(new Size((_columnWidth * span) + (Gap * (span - 1)), double.PositiveInfinity));
            if (_rowHeights.Count <= row)
            {
                _rowHeights.Add(0);
            }

            _rowHeights[row] = Math.Max(_rowHeights[row], child.DesiredSize.Height);
            _layout.Add((child, row, column, span));
            column += span;
        }

        var height = _rowHeights.Sum() + (Gap * Math.Max(0, _rowHeights.Count - 1));
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var tops = new double[_rowHeights.Count];
        for (var i = 1; i < tops.Length; i++)
        {
            tops[i] = tops[i - 1] + _rowHeights[i - 1] + Gap;
        }

        foreach (var (child, row, column, span) in _layout)
        {
            child.Arrange(new Rect(column * (_columnWidth + Gap), tops[row], (_columnWidth * span) + (Gap * (span - 1)), _rowHeights[row]));
        }

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                child.Arrange(default);
            }
        }

        return finalSize;
    }
}
