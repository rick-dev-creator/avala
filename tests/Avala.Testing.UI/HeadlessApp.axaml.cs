using Avala.Components.UI;
using Avala.Sdk.UI;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;

namespace Avala.Testing.UI;

public sealed partial class HeadlessApp : Application
{
    public static ViewRegistry Views { get; } = CreateViews();

    public static bool Renders { get; } = Environment.GetEnvironmentVariable("AVALA_HEADLESS_RENDER") == "1";

    public static AppBuilder BuildAvaloniaApp() =>
        Renders
            ? AppBuilder.Configure<HeadlessApp>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .WithInterFont()
            : AppBuilder.Configure<HeadlessApp>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
                .WithInterFont();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        DataTemplates.Add(Views);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AnimationClock.HoldStill();
        base.OnFrameworkInitializationCompleted();
    }

    private static ViewRegistry CreateViews()
    {
        var views = new ViewRegistry();
        views.AddComponentViews();

        return views;
    }
}
