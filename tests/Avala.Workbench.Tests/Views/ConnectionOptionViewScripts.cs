using Avala.Testing.UI;
using Avala.Workbench.NewJob;

namespace Avala.Workbench.Tests.Views;

public sealed class ConnectionOptionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AConnectionShowsItsNameWithItsReadingInAmberNearItsLimitAndNoReadingWhenItHasNoneAsync() =>
        ui.RunAsync(() =>
        {
            var near = Screen.Show(new DesignConnectionOptionViewModel("claude", "99% of the 7-day window used", true));
            var auto = Screen.Show(new DesignConnectionOptionViewModel(NewJobPhrases.Auto, string.Empty, false));

            Assert.Equal(("claude", "99% of the 7-day window used", true), (near.TextOf("Connection"), near.TextOf("Reading"), near.HasClass("Reading", "attention")));
            Assert.Equal((NewJobPhrases.Auto, false), (auto.TextOf("Connection"), auto.Shows("Reading")));
        }, TestContext.Current.CancellationToken);
}
