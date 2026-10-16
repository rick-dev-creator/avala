using Avala.Testing;
using Avala.Testing.UI;
using Avala.Workbench.Overview;
using Avala.Workbench.Tests.Views;

namespace Avala.Workbench.Tests.Overview;

public sealed class RecentActionViewModelScripts
{
    [Fact]
    public void ARecentActionSaysWhatTheAgentDidAndWhenItStarted() =>
        ViewModelScript.Given(new RecentActionViewModel("Run go test ./...", DateTimeOffset.UnixEpoch.AddMinutes(3), true))
            .Then(action => Assert.Equal(("Run go test ./...", DateTimeOffset.UnixEpoch.AddMinutes(3), true), (action.Text, action.Since, action.HasSince)));
}

public sealed class RecentActionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ARecentActionShowsItsTextAndItsAgeOnlyWhenItsStartIsKnownAsync() =>
        ui.RunAsync(() =>
        {
            var known = Screen.Show(new RecentActionViewModel("Run go test ./...", DateTimeOffset.UtcNow, true));
            var unknown = Screen.Show(new DesignRecentActionViewModel());

            Assert.Equal(("Run go test ./...", true), (known.TextOf("Action"), known.Shows("Age")));
            Assert.Equal(("Edit internal/payments/refund.go", false), (unknown.TextOf("Action"), unknown.Shows("Age")));
        }, TestContext.Current.CancellationToken);
}
