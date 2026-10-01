using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenGG.Desktop.Controls;

public sealed class DevicePortrait : Image
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(nameof(Model), typeof(string), typeof(DevicePortrait),
        new PropertyMetadata(null, (d, e) => ((DevicePortrait)d).Source = (string?)e.NewValue == "kbd-steelseries-apex-pro-tkl-gen3"
            ? new BitmapImage(new Uri("pack://application:,,,/OpenGG;component/Assets/Devices/keyboard.png")) : DeviceArt.For("keyboard")));
    public DevicePortrait() { Stretch = Stretch.Uniform; IsHitTestVisible = false; RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality); }
    public string? Model { get => (string?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
}
