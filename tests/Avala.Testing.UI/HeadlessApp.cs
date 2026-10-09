using Avala.Components.UI;
using Avala.Sdk.UI;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;

namespace Avala.Testing.UI;

public sealed class HeadlessApp : Application
{
    public static ViewRegistry Views { get; } = CreateViews();

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            .WithInterFont();

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        DataTemplates.Add(Views);
        Styles.Add(new StyleInclude(new Uri("avares://Avala.Testing.UI/"))
        {
            Source = new Uri("avares://Avala.Components.UI/Theme/AvalaTheme.axaml"),
        });
    }

    private static ViewRegistry CreateViews()
    {
        var views = new ViewRegistry();
        views.AddComponentViews();

        return views;
    }
}
