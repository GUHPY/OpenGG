using System.Windows;
using System.Windows.Media;

namespace OpenGG.Desktop.Controls;

/// <summary>
/// Propriedades anexadas usadas pelos templates do tema (equivalem às "props" dos componentes React):
/// ícone, raio da borda, cor de destaque, texto de dica, subtítulo.
/// </summary>
public static class Ui
{
    public static readonly DependencyProperty CompactProperty = DependencyProperty.RegisterAttached(
        "Compact", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetCompact(DependencyObject d) => (bool)d.GetValue(CompactProperty);
    public static void SetCompact(DependencyObject d, bool value) => d.SetValue(CompactProperty, value);

    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(Geometry), typeof(Ui), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    public static Geometry? GetIcon(DependencyObject d) => (Geometry?)d.GetValue(IconProperty);

    public static void SetIcon(DependencyObject d, Geometry? value) => d.SetValue(IconProperty, value);

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Ui), new PropertyMetadata(new CornerRadius(8)));

    public static CornerRadius GetCornerRadius(DependencyObject d) => (CornerRadius)d.GetValue(CornerRadiusProperty);

    public static void SetCornerRadius(DependencyObject d, CornerRadius value) => d.SetValue(CornerRadiusProperty, value);

    /// <summary>Cor de destaque local (ex.: rose-500 no Rapid Trigger, cor do LED no card).</summary>
    public static readonly DependencyProperty AccentProperty = DependencyProperty.RegisterAttached(
        "Accent", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    public static Brush? GetAccent(DependencyObject d) => (Brush?)d.GetValue(AccentProperty);

    public static void SetAccent(DependencyObject d, Brush? value) => d.SetValue(AccentProperty, value);

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(Ui), new PropertyMetadata(null));

    public static string? GetPlaceholder(DependencyObject d) => (string?)d.GetValue(PlaceholderProperty);

    public static void SetPlaceholder(DependencyObject d, string? value) => d.SetValue(PlaceholderProperty, value);

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.RegisterAttached(
        "Subtitle", typeof(string), typeof(Ui), new PropertyMetadata(null));

    public static string? GetSubtitle(DependencyObject d) => (string?)d.GetValue(SubtitleProperty);

    public static void SetSubtitle(DependencyObject d, string? value) => d.SetValue(SubtitleProperty, value);

    /// <summary>Atalho mostrado em capas de tecla dentro da dica do controle (ex.: "Ctrl+Z").</summary>
    public static readonly DependencyProperty ShortcutProperty = DependencyProperty.RegisterAttached(
        "Shortcut", typeof(string), typeof(Ui), new PropertyMetadata(null));

    public static string? GetShortcut(DependencyObject d) => (string?)d.GetValue(ShortcutProperty);

    public static void SetShortcut(DependencyObject d, string? value) => d.SetValue(ShortcutProperty, value);

    /// <summary>
    /// Id que a busca usa para chegar até este elemento (A30): a chave do controle, o id da configuração, do sensor ou
    /// do dispositivo. Ao escolher o resultado, o shell rola até ele e acende o contorno (<see cref="Spotlight"/>).
    /// </summary>
    public static readonly DependencyProperty AnchorProperty = DependencyProperty.RegisterAttached(
        "Anchor", typeof(string), typeof(Ui), new PropertyMetadata(null));

    public static string? GetAnchor(DependencyObject d) => (string?)d.GetValue(AnchorProperty);

    public static void SetAnchor(DependencyObject d, string? value) => d.SetValue(AnchorProperty, value);

    /// <summary>
    /// Todo card do tema vira widget possível do Início sozinho (estilo Card e derivados, Tile, superfície de card com
    /// canto de 16 px ou mais): nada a declarar num card novo. False tira da gaveta "Adicionar card" o que só parece
    /// card (aviso passageiro, "carregando…"). O título na gaveta é o AutomationProperties.Name, ou o título do card.
    /// </summary>
    public static readonly DependencyProperty HomeCardProperty = DependencyProperty.RegisterAttached(
        "HomeCard", typeof(bool), typeof(Ui), new PropertyMetadata(true));

    public static bool GetHomeCard(DependencyObject d) => (bool)d.GetValue(HomeCardProperty);

    public static void SetHomeCard(DependencyObject d, bool value) => d.SetValue(HomeCardProperty, value);

    /// <summary>Caixa de filtro da página: Ctrl+F põe o foco nela (sem ela, abre a paleta).</summary>
    public static readonly DependencyProperty PageSearchProperty = DependencyProperty.RegisterAttached(
        "PageSearch", typeof(bool), typeof(Ui), new PropertyMetadata(false));

    public static bool GetPageSearch(DependencyObject d) => (bool)d.GetValue(PageSearchProperty);

    public static void SetPageSearch(DependencyObject d, bool value) => d.SetValue(PageSearchProperty, value);

    /// <summary>Primeiro descendente visual com a âncora pedida (percorre também o que está fora da tela).</summary>
    public static FrameworkElement? FindAnchor(DependencyObject root, string anchor)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(anchor);
        return Descendants(root).OfType<FrameworkElement>().FirstOrDefault(e => string.Equals(GetAnchor(e), anchor, StringComparison.Ordinal));
    }

    /// <summary>Primeiro descendente visual marcado com <see cref="PageSearchProperty"/> que está à vista.</summary>
    public static UIElement? FindPageSearch(DependencyObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return Descendants(root).OfType<UIElement>().FirstOrDefault(e => GetPageSearch(e) && e.IsVisible && e.IsEnabled);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var pending = new Queue<DependencyObject>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            yield return current;
            var count = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetChildrenCount(current) : 0;
            for (var i = 0; i < count; i++)
            {
                pending.Enqueue(VisualTreeHelper.GetChild(current, i));
            }
        }
    }
}

/// <summary>
/// Página que sabe mostrar um item da busca por conta própria (abrir a aba certa, expandir o grupo, trocar o filtro)
/// antes de o shell rolar até ele. Devolve verdadeiro quando já cuidou de tudo.
/// </summary>
public interface IRevealTarget
{
    bool Reveal(string anchor);
}
