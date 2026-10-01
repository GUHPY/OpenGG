using System.Runtime.InteropServices;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using OneRGB.Application.Control;
using OneRGB.Application.Devices.Keyboard;
using OneRGB.Application.Lighting;
using OpenGG.Desktop;
using OpenGG.Desktop.Controls;
using OpenGG.Desktop.ViewModels;
using OpenGG.Desktop.Views.Pages;

// Run explicitly on an interactive Windows desktop. This check never edits or saves an onboard profile.
internal class Program
{
    [STAThread]
    static int Main()
    {
        if (Mutex.TryOpenExisting(@"Local\OpenGG.Desktop",out var instance))
        { instance.Dispose(); Console.Error.WriteLine("Close OpenGG before running the desktop checks."); return 2; }
        var saved = new[] { "settings.json", "profiles.json" }.ToDictionary(n => Path.Combine(AppHost.DataDirectory,n),n =>
            File.Exists(Path.Combine(AppHost.DataDirectory,n)) ? File.ReadAllBytes(Path.Combine(AppHost.DataDirectory,n)) : null);
        var app = new App(); app.InitializeComponent();
        var result = 1;
        app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,async () =>
        {
            try { await Check((MainWindow)app.MainWindow); result = 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { app.MainWindow?.Close(); app.Shutdown(); }
        });
        try { app.Run(); }
        finally { foreach (var (path,bytes) in saved) { if (bytes is not null) File.WriteAllBytes(path,bytes); else if (File.Exists(path)) File.Delete(path); } }
        return result;
    }

    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
    static T Named<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    static IEnumerable<DependencyObject> Tree(DependencyObject node)
    {
        yield return node;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Tree(VisualTreeHelper.GetChild(node,i))) yield return child;
    }
    static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    static async Task Check(MainWindow window)
    {
        await Task.Delay(400);
        var main = (MainViewModel)window.DataContext;
        var sidebar = Named<Border>(window,"Sidebar");
        var toggle = Named<Button>(window,"SidebarToggle");
        var nav = Named<RadioButton>(window,"DevicesNav");
        var icon = (Icon)nav.Template.FindName("Ico",nav);
        var surface = (Border)nav.Template.FindName("IconSurface",nav);
        var brand = Named<Image>(window,"SidebarBrand");
        var version = Named<TextBlock>(window,"SidebarVersion");
        var handle = new WindowInteropHelper(window).Handle;
        var region = CreateRectRgn(0,0,0,0);
        try { Require(GetWindowRgn(handle,region) > 1 && !PtInRegion(region,0,0) && PtInRegion(region,40,40),"native window corners exclude the rectangular background"); }
        finally { DeleteObject(region); }
        Require(window.WindowStyle == WindowStyle.None,"integrated window controls without the standard title bar");
        Require(window.Icon is not null && brand.Source is BitmapSource { PixelWidth: 900 },"the owner logos are embedded in the window icon and sidebar");
        var width = icon.ActualWidth; var circle = surface.ActualWidth;
        Click(toggle); await Task.Delay(35);
        if (Motion.Enabled) { Require(sidebar.Width > 76 && sidebar.Width < 224,"sidebar has an intermediate slide position"); Require(brand.Width == 150 && version.Width == 150,"brand and version keep their width during collapse"); }
        await Task.Delay(330);
        Require(Math.Abs(sidebar.Width-76) < .1 && icon.ActualWidth == width && surface.ActualWidth == circle,"collapsed sidebar keeps the icon and circle size");
        Require(version.Visibility == Visibility.Collapsed,"collapsed version is hidden after fading");
        Require(Named<Grid>(window,"NavSelection").Visibility == Visibility.Visible && surface.Background is SolidColorBrush { Color: var selectedColor } && selectedColor == Color.FromRgb(244,244,246),"compact navigation preserves the white selection circle and black contour");
        Require(((Border)nav.Template.FindName("Hover",nav)).Visibility == Visibility.Collapsed,"compact navigation removes the hover pill");
        var toggleCenter = toggle.TranslatePoint(new Point(toggle.ActualWidth/2,0),window).X;
        var iconCenter = surface.TranslatePoint(new Point(surface.ActualWidth/2,0),window).X;
        Require(Math.Abs(toggleCenter-iconCenter) < 1,$"compact sidebar toggle aligns with the navigation icons ({toggleCenter:F1}/{iconCenter:F1})");
        nav.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0) { RoutedEvent=Mouse.MouseEnterEvent }); await Task.Delay(180);
        var hoverScale = (ScaleTransform)nav.Template.FindName("IconScale",nav);
        Require(Math.Abs(hoverScale.ScaleX-1.12) < .01,"compact hover expands the icon smoothly without a pill");
        nav.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0) { RoutedEvent=Mouse.MouseLeaveEvent }); await Task.Delay(180);
        Require(Math.Abs(hoverScale.ScaleX-1) < .01,"compact hover restores the icon scale on exit");
        Click(toggle); await Task.Delay(330);
        Require(Math.Abs(sidebar.Width-224) < .1 && icon.ActualWidth == width && surface.ActualWidth == circle && version.Opacity == 1,"expanded sidebar restores labels with constant icon size");
        Click(toggle); await Task.Delay(35); Click(toggle); await Task.Delay(380);
        Require(Math.Abs(sidebar.Width-224) < .1 && !Ui.GetCompact(sidebar),"a reversed sidebar animation finishes in the latest requested state");
        var selection = Named<TranslateTransform>(window,"NavSelectionOffset");
        Named<RadioButton>(window,"DiagnosticsNav").IsChecked = true; await Task.Delay(330);
        var expectedNav = Named<RadioButton>(window,"DiagnosticsNav").TranslatePoint(default,Named<Grid>(window,"NavTrack")).Y;
        Require(Math.Abs(selection.Y-expectedNav) < 1,$"black navigation contour follows Diagnostics ({selection.Y:F1}/{expectedNav:F1})");
        var toolButtons = Tree(window).OfType<Button>().Where(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Install or prepare")).ToArray();
        Require(toolButtons.Length == 9 && toolButtons.All(b => b.Command is not null),"all nine research-tool actions have commands");
        if (main.Tools.Single(t => t.Id == "python").CanInstall)
        {
            main.InstallToolCommand.Execute(main.Tools.Single(t => t.Id == "python"));
            var timeout = DateTime.UtcNow.AddSeconds(20);
            while (!main.ToolInstallStatus.Contains("completed") && !main.ToolInstallStatus.Contains("failed") && DateTime.UtcNow < timeout) { await Task.Delay(100); }
            Require(main.ToolInstallStatus.Contains("completed"),"Install action successfully prepares local Python through Windows PowerShell");
        }
        nav.IsChecked = true; await Task.Delay(330);
        var scan = main.ScanManuallyAsync();
        Require(main.IsScanning,"manual scan exposes its progress state");
        var scanIcon = Tree(Named<Button>(window,"ScanButton")).OfType<Icon>().Single(i => BindingOperations.GetBinding(i,Motion.SpinProperty)?.Path?.Path == "IsScanning");
        Require(Motion.GetSpin(scanIcon),"refresh glyph spins during manual scan");
        await scan; Require(!main.IsScanning && !Motion.GetSpin(scanIcon),"scan feedback stops after completion");
        Require(!Tree(window).OfType<TextBox>().Any(t => BindingOperations.GetBinding(t,TextBox.TextProperty)?.Path?.Path == "Query"),"Devices has no text search field");
        Require(Named<Button>(window,"ScanButton").TranslatePoint(default,window).Y >= 22,"the scan button sits comfortably below the window's top edge");
        await main.ScanAsync(); await Task.Delay(150);
        if (main.Cards.Where(c => c.Device.HasVerifiedTransport).OrderBy(c => c.Device.ProductId == 0x1646 ? 0 : 1).FirstOrDefault() is { } card)
        {
            await main.RefreshBatteryAsync();
            var batteryDeadline = DateTime.UtcNow.AddSeconds(6);
            while (!card.HasBattery && DateTime.UtcNow < batteryDeadline) await Task.Delay(100);
            Require(card.HasBattery && card.BatteryPercent is >= 0 and <= 100,"the verified receiver supplies a live battery estimate to its device card");
            var remembered = new DeviceCardViewModel(card.Device); remembered.Update(card.Device);
            remembered.UpdateBattery(25,false); var firstMetric = remembered.Metric;
            remembered.UpdateBattery(30,true);
            Require(firstMetric == "25%" && remembered.Metric == "30%" && remembered.IsCharging && remembered.BatteryText.Contains("Charging"),"battery changes update the percentage, state and caption together");
            remembered.Update(null);
            Require(!remembered.HasBattery && remembered.BatteryPercent is null && !remembered.IsCharging,"disconnection clears stale battery and charging readings");
            main.OpenDeviceCommand.Execute(card); await Task.Delay(400); window.UpdateLayout();
            var content = Named<ContentControl>(window,"DeviceContent");
            var pages = ((StackPanel)content.Content).Children.OfType<UserControl>().ToArray();
            Require(pages[0] is ApexLightingPage && pages[1] is KeyboardPage,"RGB precedes keyboard settings");
            Require(!pages.Any(p => p.Content is ScrollViewer),"device pages share one outer scroll viewer");
            Require(!Tree(pages[0]).OfType<TextBlock>().Any(t => t.Text.Contains("USB HID direto") || t.Text.Contains("prévia ao vivo")),"RGB shows its controls without the imported expert descriptions");
            var grid = (ResponsiveGrid)pages[0].Content;
            var leftCard = ((Grid)grid.Children[0]).Children.OfType<Border>().Last();
            var leftBottom = leftCard.TranslatePoint(new Point(0,leftCard.ActualHeight),grid).Y;
            var rightCard = (Border)grid.Children[1];
            var rightBottom = rightCard.TranslatePoint(new Point(0,rightCard.ActualHeight),grid).Y;
            Require(Math.Abs(leftBottom-rightBottom) < 1,"RGB effect card aligns with the bottom of the left control card");
            var viewer = Tree(window).OfType<ScrollViewer>().Single(v => v.Content is StackPanel p && p.Children.Contains(content));
            viewer.ScrollToTop(); await Task.Delay(100);
            var source = Tree(pages[0]).OfType<TextBlock>().First(t => t.Text == "Lighting effect");
            source.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
            await Task.Delay(80);
            Require(Math.Abs(viewer.VerticalOffset-144) < 1,"wheel over a card scrolls the page by 144 pixels");
            var lighting = (ApexLightingViewModel)pages[0].DataContext;
            lighting.Controlled = false;
            var keyboard = (KeyboardViewModel)pages[1].DataContext;
            Require(ReferenceEquals(lighting.Keyboard,keyboard),"both views share the same OLED and keyboard configuration");
            Require(!Tree(pages[1]).OfType<TextBlock>().Any(t => t.Text.Contains("tecla(s) fora do padrão") || t.Text.StartsWith("Priority quando") || t.Text.StartsWith("Um passo por linha") || t.Text == "Prévia" || t.Text == "Perfis internos do teclado"),"boxed helper texts and the separate profile/preview cards are removed");
            Require(!Tree(window).OfType<Button>().Any(b => Equals(b.Content,"Abrir diagnóstico deste teclado")),"the redundant detail diagnostic button is removed");
            var sliders = Tree(pages[1]).OfType<Slider>().Where(s => s.Orientation == Orientation.Vertical).ToArray();
            Require(sliders.Length == 3,"the keyboard map has three vertical sliders");
            var rapidButton = (Button)pages[1].FindName("RapidToolButton");
            var protectionButton = (Button)pages[1].FindName("ProtectionToolButton");
            Require(rapidButton.TranslatePoint(default,pages[1]).Y < sliders[1].TranslatePoint(default,pages[1]).Y && Math.Abs(rapidButton.TranslatePoint(new Point(rapidButton.ActualWidth/2,0),pages[1]).X-sliders[1].TranslatePoint(new Point(sliders[1].ActualWidth/2,0),pages[1]).X) < 1 && protectionButton.TranslatePoint(default,pages[1]).Y < sliders[2].TranslatePoint(default,pages[1]).Y,"paint buttons sit directly above their matching sliders on the right");
            foreach (var slider in sliders)
            {
                var track = (System.Windows.Controls.Primitives.Track)slider.Template.FindName("PART_Track",slider);
                Require(!track.IsDirectionReversed && track.DecreaseRepeatButton.TranslatePoint(default,track).Y > track.Thumb.TranslatePoint(default,track).Y,"vertical slider fill grows from the bottom");
            }
            var configField = typeof(KeyboardViewModel).GetField("_config",BindingFlags.NonPublic|BindingFlags.Instance)!;
            var previous = ((KeyboardConfig)configField.GetValue(keyboard)!).ToValues();
            var import = typeof(DeviceEditorViewModel).GetMethod("ImportValues",BindingFlags.NonPublic|BindingFlags.Instance)!;
            void Import(IReadOnlyDictionary<ControlKey,ControlValue> values) => import.Invoke(keyboard,[new Func<ControlKey,ControlValue?>(k => values.GetValueOrDefault(k))]);
            try
            {
                Import(KeyboardConfig.Defaults(keyboard.Device).ToValues());
                Require(sliders[0].IsEnabled && !sliders[1].IsEnabled && !sliders[2].IsEnabled,"only actuation is enabled in a fresh selection");
                Require(Ui.GetAccent(sliders[1]) is SolidColorBrush grayRapid && grayRapid.Color.R == grayRapid.Color.G && rapidButton.Foreground is SolidColorBrush grayButton && grayButton.Color.R == grayButton.Color.G && ((Icon)pages[1].FindName("RapidSliderIcon")).Foreground is SolidColorBrush grayIcon && grayIcon.Color.R == grayIcon.Color.G,"inactive sensitivity controls and paint icons are gray");
                var w = keyboard.Keys.Single(k => k.Id == "KeyW");
                var a = keyboard.Keys.Single(k => k.Id == "KeyA");
                keyboard.SetToolCommand.Execute(KeyboardTool.RapidTrigger);
                keyboard.ToggleKeyCommand.Execute(w);
                keyboard.SelectedPress = .5;
                Require(w.RapidTrigger && !w.Protected && keyboard.RapidSensitivityEnabled && ((KeyboardConfig)configField.GetValue(keyboard)!).RapidTrigger["KeyW"].PressMm == .5,"the lightning tool paints RT and its slider edits the painted key");
                keyboard.SetToolCommand.Execute(KeyboardTool.Protection);
                keyboard.ToggleKeyCommand.Execute(w);
                keyboard.SelectedProtection = 37;
                Require(w.RapidTrigger && w.Protected && ((KeyboardConfig)configField.GetValue(keyboard)!).ProtectionSensitivities["KeyW"] == 37 && !a.Protected,"Protection adds its own flag without enabling implicit default WASD keys");
                keyboard.SetToolCommand.Execute(KeyboardTool.Protection);
                var before = ((KeyboardConfig)configField.GetValue(keyboard)!).ToValues();
                keyboard.ToggleKeyCommand.Execute(a);
                Require(before.SequenceEqual(((KeyboardConfig)configField.GetValue(keyboard)!).ToValues()),"selecting a second key never copies the first key's slider values into the group");
                keyboard.SelectedActuation = 1.5;
                Require(w.ActuationText == "1.5" && a.ActuationText == "1.5","the actuation slider updates all selected analog keys");
                keyboard.SelectAllCommand.Execute(null);
                var firstAnalog = keyboard.Keys.First(k=>k.IsSelected && !string.IsNullOrEmpty(k.ActuationText));
                Require(keyboard.SelectedActuation.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture) == firstAnalog.ActuationText && keyboard.SelectedPress == .5 && keyboard.SelectedProtection == 37,"All seeds each slider from a supported key with the corresponding feature");
                keyboard.SetToolCommand.Execute(KeyboardTool.RapidTrigger);
                keyboard.ToggleKeyCommand.Execute(w);
                Require(!w.RapidTrigger && w.Protected,"painting an RT key again removes only RT");
                keyboard.SetToolCommand.Execute(KeyboardTool.Protection);
                keyboard.ToggleKeyCommand.Execute(w);
                Require(!w.RapidTrigger && !w.Protected,"painting a Protection key again removes only Protection");
                keyboard.SetToolCommand.Execute(KeyboardTool.RapidTrigger);
                keyboard.SelectAllCommand.Execute(null);
                Require(keyboard.Keys.Count(k=>k.RapidTrigger) == 60 && keyboard.SelectedCount == 60 && keyboard.SelectionText.Contains("enable Rapid Trigger") && keyboard.SelectionAccent == "#F59E0B","All paints every supported Rapid Trigger key and keeps the orange instruction");
                keyboard.SetToolCommand.Execute(KeyboardTool.Protection);
                keyboard.SelectWasdCommand.Execute(null);
                Require(keyboard.Keys.Count(k=>k.Protected) == 4 && keyboard.SelectionText.Contains("enable Protection Mode") && keyboard.SelectionAccent == "#60A5FA","WASD paints Protection and updates the blue instruction");
                keyboard.SelectAllCommand.Execute(null);
                Require(keyboard.Keys.Count(k=>k.Protected) == 60 && keyboard.Keys.Count(k=>k.RapidTrigger) == 60,"bulk Protection painting preserves Rapid Trigger");
                keyboard.ClearSelectionCommand.Execute(null);
                Require(!keyboard.Keys.Any(k=>k.Protected) && keyboard.Keys.Count(k=>k.RapidTrigger) == 60 && keyboard.SelectedCount == 0 && keyboard.SelectionText.Contains("enable Protection Mode"),"Clear removes only the current Protection feature and keeps its instruction");
                keyboard.SetToolCommand.Execute(KeyboardTool.RapidTrigger);
                keyboard.ClearSelectionCommand.Execute(null);
                Require(!keyboard.Keys.Any(k=>k.RapidTrigger) && !keyboard.RapidTriggerOnKeyboard,"Clear removes Rapid Trigger and turns off its native master flag");
                var host = (AppHost)typeof(MainWindow).GetField("_host",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
                Require(host.ApexKeyboard.LoadedSlot is null,"UI painting checks never load, send analog settings or save an onboard profile");
            }
            finally { if (keyboard.Tool != KeyboardTool.Selection) keyboard.SetToolCommand.Execute(keyboard.Tool); Import(previous); keyboard.ClearSelectionCommand.Execute(null); }
            var deviceKeyboard = Tree(pages[0]).OfType<ApexKeyboard>().Single();
            var oledTab = Tree(pages[0]).OfType<RadioButton>().Single(r => Equals(r.Content,"OLED"));
            var colorTab = Tree(pages[0]).OfType<RadioButton>().Single(r => Equals(r.Content,"Color"));
            void SelectAccessible(RadioButton radio) => ((ISelectionItemProvider)UIElementAutomationPeer.CreatePeerForElement(radio)!.GetPattern(PatternInterface.SelectionItem)).Select();
            SelectAccessible(oledTab); await window.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.DataBind);
            Require(lighting.OledSelected,"assistive-technology selection switches the OLED panel and its model together");
            SelectAccessible(colorTab); await Task.Delay(420);
            Require(!lighting.OledSelected && !deviceKeyboard.OledFocused && Tree(pages[0]).OfType<HsvPicker>().Single().IsVisible,"selecting Cor restores its picker and the keyboard camera");
            lighting.OledSelected = true; await Task.Delay(60);
            if (Motion.Enabled) Require(deviceKeyboard.ZoomProgress > 0 && deviceKeyboard.ZoomProgress < 1,$"OLED zoom has an intermediate camera position ({deviceKeyboard.ZoomProgress:F3}, focused={deviceKeyboard.OledFocused}, visible={deviceKeyboard.IsVisible}, section={main.Section})");
            lighting.OledSelected = false; await Task.Delay(30); lighting.OledSelected = true; await Task.Delay(420);
            Require(Math.Abs(deviceKeyboard.ZoomProgress-1) < .001 && ReferenceEquals(deviceKeyboard.OledImage,keyboard.OledPreview),"reversed OLED zoom finishes on the actual shared OLED bitmap");
            var stage = (Grid)VisualTreeHelper.GetParent(deviceKeyboard);
            Require(Math.Abs(deviceKeyboard.ActualWidth-stage.ActualWidth) < 1 && Math.Abs(deviceKeyboard.ActualHeight-stage.ActualHeight) < 1 && deviceKeyboard.Margin == new Thickness(0),"the OLED camera fills its entire preview stage without an inner clipping box");
            Require(Math.Abs(leftCard.ActualHeight-(leftBottom-leftCard.TranslatePoint(default,grid).Y)) < 1,"switching Color and OLED preserves the status card's height");
            Require(Tree(pages[0]).OfType<Slider>().Any(s => BindingOperations.GetBinding(s,Slider.ValueProperty)?.Path?.Path == "Brightness" && s.IsVisible) && Tree(pages[0]).OfType<Slider>().Any(s => BindingOperations.GetBinding(s,Slider.ValueProperty)?.Path?.Path == "Speed" && s.IsVisible),"brightness and speed remain visible on the OLED tab");
            lighting.OledSelected = false;
            var oldMode = lighting.Mode;
            SelectAccessible(Tree(pages[0]).OfType<RadioButton>().Single(r=>Equals(r.Content,"Static")));
            Require(lighting.Mode == LightingMode.Static,"accessible effect selection updates the typed lighting mode");
            lighting.Mode = oldMode;
            keyboard.ToolsTab = "Presets"; await Task.Delay(280); window.UpdateLayout();
            viewer.ScrollToBottom(); await Task.Delay(100);
            var bottomOffset = viewer.VerticalOffset;
            var extent = viewer.ExtentHeight;
            SelectAccessible(Tree(pages[1]).OfType<RadioButton>().Single(r=>Equals(r.Content,"Rapid Tap"))); await Task.Delay(280); window.UpdateLayout();
            Require(Tree(pages[1]).OfType<Button>().Single(b => Equals(b.Content,"Pair")).IsVisible && !Tree(pages[1]).OfType<Button>().Single(b => Equals(b.Content,"Macro")).IsVisible,"Rapid Tap occupies the shared card while the other tab is hidden");
            Require(Math.Abs(viewer.ExtentHeight-extent) < 1 && Math.Abs(viewer.VerticalOffset-bottomOffset) < 1,"empty Rapid Tap keeps the Presets height and page scroll position");
            SelectAccessible(Tree(pages[1]).OfType<RadioButton>().Single(r=>Equals(r.Content,"Macros"))); await Task.Delay(280); window.UpdateLayout();
            Require(Tree(pages[1]).OfType<Button>().Single(b => Equals(b.Content,"Macro")).IsVisible && !Tree(pages[1]).OfType<Button>().Single(b => Equals(b.Content,"Pair")).IsVisible,"Macros occupies the same card without duplicate panels");
            keyboard.ToolsTab = "Presets"; await Task.Delay(280); window.UpdateLayout();
            Require(Math.Abs(viewer.VerticalOffset-bottomOffset) < 1 && Math.Abs(viewer.ExtentHeight-extent) < 1,"returning to Presets restores its contents without another scroll");
            Require(ReferenceEquals(lighting.Device,card) && Tree(leftCard).OfType<BatteryBar>().Single().Percent == card.BatteryPercent && Tree(leftCard).OfType<TextBlock>().Any(t=>t.Text == lighting.FeedbackTitle),"battery and actual keyboard feedback share the original status card");
            Require(lighting.FeedbackTitle == "Read the profile before editing" && lighting.FeedbackText.Contains("Read and activate"),"unloaded-profile feedback is concise and explains the required action");
        }
        else { Console.WriteLine("SKIP Apex detail checks: no remembered or connected verified receiver."); }
        window.WindowState = WindowState.Maximized; await Task.Delay(150);
        var root = Named<Grid>(window,"Root");
        var work = OneRGB.Windows.WindowFrame.WorkArea(handle);
        var top = root.PointToScreen(default);
        var bottom = root.PointToScreen(new Point(root.ActualWidth,root.ActualHeight));
        Require(Math.Abs(top.X-work.Left) < 2 && Math.Abs(top.Y-work.Top) < 2 && Math.Abs(bottom.X-work.Right) < 2 && Math.Abs(bottom.Y-work.Bottom) < 2,"maximized content matches the monitor work area without covering the taskbar");
        window.WindowState = WindowState.Normal; await Task.Delay(150);
        Require(Named<Grid>(window,"Root").Margin == new Thickness(0),"restoring resets the content inset");
        var animatedBattery = new BatteryBar { Percent=25,Charging=true,Width=180,Opacity=0 };
        var phase = (DependencyProperty)typeof(BatteryBar).GetField("PhaseProperty",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        root.Children.Add(animatedBattery); window.UpdateLayout();
        try
        {
            var start = (double)animatedBattery.GetValue(phase); await Task.Delay(120);
            if(Motion.Enabled) Require((double)animatedBattery.GetValue(phase) != start,"charging battery runs its native fill animation");
            animatedBattery.Percent=100; var full = (double)animatedBattery.GetValue(phase); await Task.Delay(100);
            Require((double)animatedBattery.GetValue(phase) == full,"the charging animation stops when the battery is full");
        }
        finally { root.Children.Remove(animatedBattery); }
        CheckImages();
        Console.WriteLine("Desktop usability checks passed.");
    }
    static void CheckImages()
    {
        var pixels = new byte[128*40*4];
        for (var y=0;y<40;y++) for(var x=0;x<128;x++)
        {
            var i = (y*128+x)*4;
            pixels[i]=pixels[i+1]=pixels[i+2]=255;
            pixels[i+3] = x < 64 ? (byte)255 : (byte)0;
        }
        var input = BitmapSource.Create(128,40,96,96,PixelFormats.Bgra32,null,pixels,128*4);
        var frame = OledImage.FromBitmap(input);
        Require(frame.LitCount == 64*40 && frame.ToRowMajorMsbFirst().Length == 640 && frame[0,0] && !frame[127,0],"image import produces 128 by 40 monochrome and composites transparency onto black");
        var square = OledImage.FromBitmap(BitmapSource.Create(40,40,96,96,PixelFormats.Gray8,null,Enumerable.Repeat((byte)255,1600).ToArray(),40));
        Require(square.LitCount == 1600 && square[44,0] && !square[43,0] && !square[84,0],"OLED image fitting preserves aspect ratio and adds black side margins");
        var file = Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".png");
        try
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(input));
            using(var stream=File.Create(file)) encoder.Save(stream);
            var loaded = OledImage.Load(file);
            Require(loaded.ToRowMajorMsbFirst().SequenceEqual(frame.ToRowMajorMsbFirst()),"Open File decoder produces the same monochrome payload as its preview");
            var report = ApexProtocol.OledFrame(loaded.ToRowMajorMsbFirst());
            Require(report[1] == 0x4A && report.Length == 642 && report[2] == 255 && report[2+64] == 0,"uploaded OLED pixels use the existing verified 4A vertical-page transport");
            var gif = new GifBitmapEncoder();
            gif.Frames.Add(BitmapFrame.Create(BitmapSource.Create(40,40,96,96,PixelFormats.Gray8,null,Enumerable.Repeat((byte)255,1600).ToArray(),40)));
            gif.Frames.Add(BitmapFrame.Create(BitmapSource.Create(40,40,96,96,PixelFormats.Gray8,null,new byte[1600],40)));
            using(var stream=File.Create(file)) gif.Save(stream);
            Require(OledImage.Load(file).LitCount == 1600,"animated GIF import deliberately uses its first frame");
        }
        finally { if(File.Exists(file)) File.Delete(file); }
        var visual = new ApexKeyboard { Width=768,Height=512,IsActive=false,Mode=LightingMode.Static,Color=Colors.Cyan,Brightness=0 };
        visual.Measure(new Size(768,512)); visual.Arrange(new Rect(0,0,768,512));
        byte[] Render()
        {
            visual.UpdateLayout();
            var bitmap = new RenderTargetBitmap(768,512,96,96,PixelFormats.Pbgra32); bitmap.Render(visual);
            var data = new byte[768*512*4]; bitmap.CopyPixels(data,768*4,0); return data;
        }
        var gray = Render(); visual.Brightness=1; var full=Render(); visual.Brightness=.25; var dim=Render();
        var sample = Enumerable.Range(0,768*512).Where(i=>gray[i*4+3] == 255).MaxBy(i=>Math.Abs(full[i*4]-gray[i*4])+Math.Abs(full[i*4+1]-gray[i*4+1])+Math.Abs(full[i*4+2]-gray[i*4+2]))*4;
        Require(Enumerable.Range(0,3).All(c=>Math.Abs(dim[sample+c]-(gray[sample+c]+.25*(full[sample+c]-gray[sample+c]))) < 4) && Math.Abs(full[sample]-gray[sample]) > 20,"25 percent RGB fades over the gray keyboard instead of painting an opaque black layer");
    }
    [DllImport("gdi32.dll")] static extern nint CreateRectRgn(int l,int t,int r,int b);
    [DllImport("user32.dll")] static extern int GetWindowRgn(nint window,nint region);
    [DllImport("gdi32.dll")] static extern bool PtInRegion(nint region,int x,int y);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint handle);
}
