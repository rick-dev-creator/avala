using Avala.Testing.UI;
using Avala.Workbench.Settings;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class LimitConnectionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ARowShowsItsPlaceAndOffersToMoveUpOnlyBelowTheFirstAsync() =>
        ui.RunAsync(() =>
        {
            var first = Screen.Show(new DesignLimitConnectionViewModel());
            var second = Screen.Show(new DesignLimitConnectionViewModel { Name = "claude-max", Position = "2", CanMoveUp = true, IsIncluded = false });

            Assert.Equal(("1", true), (first.TextOf("Position"), first.Find<CheckBox>("Included").IsChecked));
            Assert.False(first.Find<Button>("MoveUp").IsVisible);
            Assert.Equal((false, true), (second.Find<CheckBox>("Included").IsChecked, second.Find<Button>("MoveUp").IsVisible));
        }, TestContext.Current.CancellationToken);
}
