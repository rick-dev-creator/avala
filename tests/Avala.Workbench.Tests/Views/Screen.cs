using Avala.Testing.UI;
using Avala.Workbench.UI;

[assembly: AssemblyFixture(typeof(HeadlessUi))]

namespace Avala.Workbench.Tests.Views;

internal static class Screen
{
    private static bool registered;

    public static ViewScript Show(object viewModel)
    {
        if (!registered)
        {
            new WorkbenchPlugin().RegisterViews(HeadlessApp.Views);
            registered = true;
        }

        return ViewScript.Show(viewModel);
    }
}
