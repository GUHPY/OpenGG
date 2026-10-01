using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using OpenGG.Desktop.Controls;
using OneRGB.Windows;
using OpenGG.Desktop.ViewModels;
using OpenGG.Desktop.Views.Pages;

namespace OpenGG.Desktop;

public partial class MainWindow : Window
{
    private readonly AppHost _host;
    private readonly MainViewModel _main;
    private readonly KeyboardViewModel _keyboard;
    private readonly ApexLightingViewModel _lighting;
    private readonly StackPanel _apex;
    private bool _collapsed;
    private int _sidebarTransition;

    [DllImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);

    private async void OnToggleSidebar(object sender, RoutedEventArgs e)
    {
        _collapsed = !_collapsed;
        var transition = ++_sidebarTransition;
        if (!_collapsed)
        {
            Ui.SetCompact(Sidebar,false);
            SidebarBrand.Visibility = SidebarVersion.Visibility = Visibility.Visible;
        }
        Motion.To(Sidebar,FrameworkElement.WidthProperty,_collapsed ? 76 : 224,Motion.Slow,CubicBezierEase.Emphasized);
        Motion.To(SidebarToggleOffset,TranslateTransform.XProperty,_collapsed ? -8 : 0,Motion.Slow,CubicBezierEase.Emphasized);
        if (!_collapsed)
        {
            foreach (var item in new[] { DevicesNav,DiagnosticsNav })
            {
                if (item.Template.FindName("IconScale",item) is ScaleTransform scale) { Motion.To(scale,ScaleTransform.ScaleXProperty,1,Motion.Fast); Motion.To(scale,ScaleTransform.ScaleYProperty,1,Motion.Fast); }
            }
        }
        foreach (var label in new FrameworkElement[] { SidebarBrand,SidebarVersion }) { Motion.To(label,OpacityProperty,_collapsed ? 0 : 1,Motion.Standard); }
        SidebarToggle.ToolTip = _collapsed ? "Expand menu" : "Collapse menu";
        if (Motion.Enabled) { await Task.Delay(Motion.Slow); }
        if (transition != _sidebarTransition) { return; }
        Ui.SetCompact(Sidebar,_collapsed);
        SidebarBrand.Visibility = SidebarVersion.Visibility = _collapsed ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnNavigationHover(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not System.Windows.Controls.RadioButton item || item.Template.FindName("IconScale",item) is not ScaleTransform scale) { return; }
        var size = Ui.GetCompact(Sidebar) && e.RoutedEvent == MouseEnterEvent ? 1.12 : 1;
        Motion.To(scale,ScaleTransform.ScaleXProperty,size,Motion.Fast);
        Motion.To(scale,ScaleTransform.ScaleYProperty,size,Motion.Fast);
    }

    private void OnNavigationChanged(object sender, RoutedEventArgs e)
    {
        if (NavTrack?.IsLoaded == true) { MoveNavigationSelection(true); }
    }

    private void MoveNavigationSelection(bool animate)
    {
        var item = DiagnosticsNav.IsChecked == true ? DiagnosticsNav : DevicesNav;
        if (!item.IsLoaded) { return; }
        var position = item.TranslatePoint(default,NavTrack);
        Motion.To(NavSelectionOffset,TranslateTransform.YProperty,position.Y,animate ? Motion.Slow : TimeSpan.Zero,CubicBezierEase.Emphasized);
    }

    private nint WndProc(nint window,int message,nint wParam,nint lParam,ref bool handled)
    {
        if (message == 0x02E0) { Dispatcher.BeginInvoke(DispatcherPriority.Render,ApplyWindowShape); }
        if (message == 0x0024) { WindowFrame.ConstrainMaximized(window,lParam); }
        if (message == 0x0084 && WindowState == WindowState.Normal && WindowFrame.CornerHit(window,lParam,(int)Math.Ceiling(32 * VisualTreeHelper.GetDpi(this).DpiScaleX)) is > 0 and var corner)
        { handled = true; return corner; }
        return 0;
    }

    private void ApplyWindowShape()
    {
        if (WindowState == WindowState.Minimized) { return; }
        var window = new WindowInteropHelper(this).Handle;
        if (window == 0) { return; }
        var dpi = VisualTreeHelper.GetDpi(this);
        var maximized = WindowState == WindowState.Maximized;
        Root.Margin = new Thickness(0);
        if (maximized)
        {
            var origin = PointToScreen(default);
            var work = WindowFrame.WorkArea(window);
            Root.Margin = new Thickness(Math.Max(0,work.Left-origin.X)/dpi.DpiScaleX,Math.Max(0,work.Top-origin.Y)/dpi.DpiScaleY,
                Math.Max(0,origin.X+ActualWidth*dpi.DpiScaleX-work.Right)/dpi.DpiScaleX,Math.Max(0,origin.Y+ActualHeight*dpi.DpiScaleY-work.Bottom)/dpi.DpiScaleY);
        }
        Sidebar.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(28,0,0,28);
        Sidebar.BorderThickness = maximized ? new Thickness(0) : new Thickness(1,1,0,1);
        MaxIcon.Glyph = (Geometry)FindResource(maximized ? "Icon.Restore" : "Icon.Maximize");
        WindowFrame.Round(window,(int)Math.Round(56 * dpi.DpiScaleX),maximized);
    }

    private void OnMinimize(object sender,RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximize(object sender,RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnClose(object sender,RoutedEventArgs e) => Close();

    public MainWindow()
    {
        InitializeComponent();
        ScrollBehavior.Install();
        Motion.Apply(false);
        SourceInitialized += (_, _) =>
        {
            var window = new WindowInteropHelper(this).Handle;
            int enabled = 1; DwmSetWindowAttribute(window,20,ref enabled,4);
            HwndSource.FromHwnd(window)?.AddHook(WndProc);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,ApplyWindowShape);
        };
        StateChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Render,ApplyWindowShape);
        SizeChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Render,ApplyWindowShape);
        _host = new AppHost();
        _main = new MainViewModel(_host, Dispatcher);
        _keyboard = new KeyboardViewModel(_host, _main, Dispatcher);
        _lighting = new ApexLightingViewModel(_host, Dispatcher, _keyboard);
        _apex = new StackPanel();
        foreach (var page in new UserControl[] { new ApexLightingPage { DataContext = _lighting },new KeyboardPage { DataContext = _keyboard } })
        {
            if (page.Content is ScrollViewer scroll)
            {
                var content = scroll.Content; scroll.Content = null; page.Content = content;
            }
            page.Margin = new Thickness(0,0,0,20); _apex.Children.Add(page);
        }
        DataContext = _main;
        _main.DeviceRequested += (_, card) =>
        {
            try { if (card is not null) _host.SelectKeyboard(card.Device); }
            catch (IOException ex) { _main.Error = ex.Message; }
            _lighting.Device = card;
            _lighting.IsActive = card?.Device.HasVerifiedTransport == true;
            if (card is null) { DeviceContent.Content = null; return; }
            if (card.Device.HasVerifiedTransport) { DeviceContent.Content = _apex; }
            else
            {
                var panel = new StackPanel { Margin = new Thickness(8, 20, 8, 20) };
                panel.Children.Add(new TextBlock { Text = "This model has a diagnostic interface. The Apex Pro TKL Gen 3 editor is available on the verified model.",
                    Style = (Style)FindResource("Text.Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,16,0,0) });
                DeviceContent.Content = panel;
            }
        };
        Loaded += async (_, _) => { MoveNavigationSelection(false); await _main.ScanAsync(); };
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.F5) { e.Handled = true; await _main.ScanManuallyAsync(); }
            if (e.Key == Key.Left && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && _main.HasDetail) { e.Handled = true; _main.BackCommand.Execute(null); }
        };
        Closed += (_, _) => { _keyboard.StopUpdates(); _main.Dispose(); _lighting.Dispose(); _host.Dispose(); };
    }
}
