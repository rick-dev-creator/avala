using Avala.Testing.UI;
using Avala.Workbench.Docs;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class C4NodeViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ANodeShowsItsNameKindAndTechnologyAndMarksANewElementAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignC4NodeViewModel());

            Assert.Equal(("Payments", "Container", "Go · stripe-go v79"), (view.TextOf("NodeLabel"), view.TextOf("NodeKind"), view.TextOf("NodeTechnology")));
            Assert.Equal((true, true), (view.Find<Border>("Node").Classes.Contains("fresh"), view.Find<Border>("Node").Classes.Contains("focus")));
        }, Cancellation);
}
