using Avala.Testing.UI;
using Avala.Workbench.Decisions;
using Avala.Workbench.Navigation;
using Avala.Workbench.Review;
using Avala.Workbench.Settings;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Usage;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Avala.Workbench.Tests.Views;

public sealed class ThemeScreenScripts(HeadlessUi ui)
{
    private const double Legible = 3;

    public static TheoryData<string, string> Screens =>
        new MatrixTheoryData<string, string>(["sidebar", "conversation", "settings", "review", "decisions", "usage"], ["Dark", "Light"]);

    [Theory]
    [MemberData(nameof(Screens))]
    public Task EveryKeyScreenDrawsItsVariantAndKeepsItsTextLegibleAsync(string screen, string name) =>
        ui.RunAsync(() =>
        {
            var variant = name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
            var view = Screen.Show(Of(screen), variant, 1440, 1000);
            var surface = Legibility.Resource("SurfaceWindowBrush", variant);

            var texts = Legibility.Texts(view.Window, surface);

            Assert.Equal(variant, view.Window.ActualThemeVariant);
            Assert.Equal(surface, ((ISolidColorBrush)view.Window.Background!).Color);
            Assert.NotEmpty(texts);
            Assert.All(texts, text => Assert.True(text.Ratio >= Legible, $"\"{text.Text}\" is {text.Foreground} on {text.Background}: {text.Ratio:F2}:1"));
        }, TestContext.Current.CancellationToken);

    private static object Of(string screen) => screen switch
    {
        "sidebar" => new DesignSidebarViewModel(),
        "conversation" => new DesignWorkbenchViewModel(),
        "settings" => new DesignSettingsViewModel(),
        "review" => new DesignReviewViewModel(),
        "decisions" => new DesignDecisionsViewModel(),
        _ => new DesignUsageViewModel(),
    };
}
