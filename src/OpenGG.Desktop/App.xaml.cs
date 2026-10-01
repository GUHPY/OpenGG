using System.IO;
using System.Windows;

namespace OpenGG.Desktop;

public partial class App : System.Windows.Application
{
    private Mutex? _instance;

    protected override void OnExit(ExitEventArgs e) { if (_instance is not null) { _instance.ReleaseMutex(); _instance.Dispose(); } base.OnExit(e); }

    protected override void OnStartup(StartupEventArgs e)
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        base.OnStartup(e);
        if (e.Args.Contains("--inspect"))
        {
            var index = Array.IndexOf(e.Args,"--out");
            if (index < 0 || index + 1 >= e.Args.Length) { MessageBox.Show("Use OpenGG.exe --inspect --out inventory.json"); Shutdown(1); return; }
            try
            {
                var all = OneRGB.Windows.HidEnumerator.Enumerate();
                var inventory = new { timestamp = DateTimeOffset.UtcNow, keyboards = SteelSeriesKeyboards.Discover(all),
                    hid = all.Select(i => new { identity=i.Identity.ToString(),i.Product,i.Manufacturer,i.VersionNumber,i.ContainerId,i.InputReportLength,i.OutputReportLength,i.FeatureReportLength,i.Error }) };
                File.WriteAllText(e.Args[index+1],System.Text.Json.JsonSerializer.Serialize(inventory,AppHost.Json));
                Shutdown(0);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message,"OpenGG inventory"); Shutdown(1); }
            return;
        }
        var mutex = new Mutex(true,@"Local\OpenGG.Desktop",out var created);
        if (!created) { mutex.Dispose(); MessageBox.Show("OpenGG is already running.","OpenGG"); Shutdown(0); return; }
        _instance = mutex;
        DispatcherUnhandledException += (_, error) =>
        {
            Directory.CreateDirectory(AppHost.DataDirectory);
            File.AppendAllText(Path.Combine(AppHost.DataDirectory, "errors.log"), DateTimeOffset.UtcNow + " " + error.Exception + Environment.NewLine);
            if (MainWindow?.DataContext is ViewModels.MainViewModel main) { main.Error = error.Exception.Message; }
            else { MessageBox.Show(error.Exception.Message, "OpenGG", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
            error.Handled = true;
        };
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
