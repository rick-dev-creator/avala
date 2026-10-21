using Avala.Testing.UI;
using Avala.Workbench.Overview;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Avala.Workbench.Tests.Views;

public sealed class AgentTreeRoundThreeViewScripts(HeadlessUi ui)
{
    private static readonly string[] Names = ["Title", "AgentName"];

    [Fact]
    public Task OverviewNeverNarrowsACardSoFarThatItsNameCannotBeReadAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDelegationViewModel(), ThemeVariant.Dark, 1024, 1100);

            var cramped = view.All<TextBlock>()
                .Where(name => Names.Contains(name.Name) && name.IsEffectivelyVisible && name.Bounds.Width < 100 && name.TextLayout.TextLines.Any(line => line.HasCollapsed))
                .Select(name => $"{name.Text} gets {name.Bounds.Width:0} px")
                .ToList();

            Assert.Empty(cramped);
        }, TestContext.Current.CancellationToken);
}
