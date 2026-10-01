using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenGG.Desktop.Mvvm;

/// <summary>Base mínima de view model (sem pacote externo — política de dependências da spec §459).</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => execute(parameter);
}

/// <summary>bool → Visible/Collapsed. Parâmetro "!" inverte.</summary>
public sealed class BoolToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var on = value switch
        {
            bool b => b,
            null => false,
            string s => s.Length > 0,
            int i => i != 0,
            _ => true,
        };
        if (parameter is "!")
        {
            on = !on;
        }
        else if (parameter is string equals)
        {
            on = string.Equals(value?.ToString(), equals, StringComparison.Ordinal); // "app" → só quando o valor é "app"
        }

        return on ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InverseBool : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>Color ou "#RRGGBB" → SolidColorBrush congelado.</summary>
public sealed class ToBrush : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = value switch
        {
            Color c => c,
            string s when !string.IsNullOrWhiteSpace(s) => (Color)ColorConverter.ConvertFromString(s),
            _ => Colors.Transparent,
        };
        if (parameter is string alpha && byte.TryParse(alpha, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a))
        {
            color.A = a;
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Cor → sombra brilhante (halo do LED no DeviceCard: box-shadow 0 2px 10px cor88).</summary>
public sealed class ToGlow : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = value switch
        {
            Color c => c,
            string s when !string.IsNullOrWhiteSpace(s) => (Color)ColorConverter.ConvertFromString(s),
            _ => Colors.Transparent,
        };
        // Parâmetro "raio,opacidade" (ex.: "12,0.15" = 0 0 12px cor26 do DeviceCard).
        var parts = (parameter as string ?? "12").Split(',');
        var radius = double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 12;
        var opacity = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var o) ? o : 0.75;
        var effect = new System.Windows.Media.Effects.DropShadowEffect { Color = color, BlurRadius = radius, ShadowDepth = 0, Opacity = opacity };
        effect.Freeze();
        return effect;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Chave de recurso (ex.: "Icon.Cpu") → recurso da aplicação.</summary>
public sealed class ResourceKeyToValue : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key ? System.Windows.Application.Current.TryFindResource(key) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Igualdade para RadioButton ↔ enum/string: IsChecked = (valor == parâmetro).</summary>
public sealed class EqualsParameter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true ? Binding.DoNothing : targetType.IsEnum && Enum.TryParse(targetType,parameter?.ToString(),out var parsed) ? parsed! : parameter!;
}

/// <summary>Multiplica um double pelo parâmetro (ex.: 0–1 → largura em pixels).</summary>
public sealed class Multiply : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var v = value is double d ? d : System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
        var k = double.Parse((string)parameter, CultureInfo.InvariantCulture);
        return v * k;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Dois valores iguais (sem diferenciar maiúsculas) → "selected"; usado no swatch de cor ativo.</summary>
public sealed class SameTag : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [string a, string b] && string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ? "selected" : null;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => [];
}

/// <summary>
/// Leva o DataContext da página para quem não está na árvore visual (colunas do DataGrid): declare como recurso com
/// <c>Data="{Binding}"</c> e use <c>{Binding Data.Expert, Source={StaticResource Proxy}}</c>.
/// </summary>
public sealed class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy));

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
