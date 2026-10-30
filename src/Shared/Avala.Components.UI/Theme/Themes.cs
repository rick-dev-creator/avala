using Avala.Sdk.Appearance;
using Avalonia.Styling;

namespace Avala.Components.UI.Theme;

public static class Themes
{
    public static readonly ThemeVariant Tide = new(nameof(Tide), ThemeVariant.Dark);

    public static readonly ThemeVariant Unit = new(nameof(Unit), ThemeVariant.Dark);

    public static readonly ThemeVariant Ash = new(nameof(Ash), ThemeVariant.Dark);

    public static readonly ThemeVariant Bloom = new(nameof(Bloom), ThemeVariant.Light);

    public static readonly ThemeVariant Signal = new(nameof(Signal), ThemeVariant.Dark);

    public static bool IsDark(ThemeVariant variant) =>
        variant == ThemeVariant.Dark || (variant.InheritVariant is { } basis && IsDark(basis));

    public static ThemeVariant Of(ThemeChoice theme) => theme switch
    {
        ThemeChoice.Light => ThemeVariant.Light,
        ThemeChoice.Dark => ThemeVariant.Dark,
        ThemeChoice.Tide => Tide,
        ThemeChoice.Unit => Unit,
        ThemeChoice.Ash => Ash,
        ThemeChoice.Bloom => Bloom,
        ThemeChoice.Signal => Signal,
        _ => ThemeVariant.Default,
    };
}
