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

    public static ViewScript ShowScrolled(object viewModel, double width, double height)
    {
        Register();
        var content = new StackPanel { Children = { new ContentControl { Name = "Scrolled", Height = height, Content = viewModel }, new Border { Height = height * 2 } } };
        var window = new Window { Width = width, Height = height, Content = new ScrollViewer { Name = "Pane", Content = content } };

        return ViewScript.Present(window, viewModel);
    }

    public static ViewScript ShowStill(object viewModel, double height)
    {
        Register();
        var window = new Window { Width = 640, Height = height, Content = viewModel };
        Components.UI.Theme.Motion.SetIsReduced(window, true);

        return ViewScript.Present(window, viewModel);
    }

    public static ViewScript ShowStill(object viewModel, ThemeVariant variant, double width, double height)
    {
        Register();
        var window = new Window { Width = width, Height = height, RequestedThemeVariant = variant, Content = viewModel };
        window.Bind(Window.BackgroundProperty, window.GetResourceObservable("SurfaceWindowBrush"));
        Components.UI.Theme.Motion.SetIsReduced(window, true);

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
