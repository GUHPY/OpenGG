using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenGG.Desktop.Controls;

public static class ScrollBehavior
{
    private static bool _installed;
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel));
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        var viewers = new List<ScrollViewer>();
        var inWidget = false;
        for (var node = e.OriginalSource as DependencyObject; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is AdaptiveCardFrame) { inWidget = true; viewers.Clear(); }
            if (node is ScrollViewer viewer) viewers.Add(viewer);
        }
        // Home widgets share their page's wheel; elsewhere a nested list scrolls until it reaches its edge.
        var target = viewers.FirstOrDefault(v => v.ScrollableHeight > 0 && (e.Delta > 0 ? v.VerticalOffset > 0 : v.VerticalOffset < v.ScrollableHeight));
        if (target is null) { if (inWidget) e.Handled = true; return; }
        // Virtualized trees and lists can delegate scrolling while still using pixel offsets.
        var items = target.TemplatedParent as ItemsControl;
        var step = target.CanContentScroll && (items is null || VirtualizingPanel.GetScrollUnit(items) == ScrollUnit.Item) ? 6 : 144;
        target.ScrollToVerticalOffset(target.VerticalOffset - e.Delta / 120.0 * step);
        e.Handled = true;
    }
}
