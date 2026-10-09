using Avala.Testing.UI;
using Avala.Workbench.UI;
using Avalonia.Controls;
using Avalonia.Styling;

[assembly: AssemblyFixture(typeof(HeadlessUi))]

namespace Avala.Workbench.Tests.Views;

internal static class Screen
{
    private static bool registered;

    public static ViewScript Show(object viewModel)
    {
        Register();

        return ViewScript.Show(viewModel);
    }

    public static ViewScript Show(object viewModel, ThemeVariant variant, double width, double height)
    {
        Register();
        var window = new Window { Width = width, Height = height, RequestedThemeVariant = variant, Content = viewModel };
        window.Bind(Window.BackgroundProperty, window.GetResourceObservable("SurfaceWindowBrush"));

        return ViewScript.Present(window, viewModel);
    }

    private static void Register()
    {
        if (!registered)
        {
            new WorkbenchPlugin().RegisterViews(HeadlessApp.Views);
            registered = true;
        }
    }
}
