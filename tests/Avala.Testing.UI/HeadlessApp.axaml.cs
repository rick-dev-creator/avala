using Avala.Components.UI;
using Avala.Sdk.UI;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;

namespace Avala.Testing.UI;

public sealed partial class HeadlessApp : Application
{
    public static ViewRegistry Views { get; } = CreateViews();

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            .WithInterFont();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        DataTemplates.Add(Views);
    }

    private static ViewRegistry CreateViews()
    {
        var views = new ViewRegistry();
        views.AddComponentViews();

        return views;
    }
}
