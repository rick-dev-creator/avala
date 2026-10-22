using Avala.Testing.UI;
using Avala.Workbench.Archive;
using Avala.Workbench.Overview;
using Avala.Workbench.Presenting;
using Avala.Workbench.Resources;
using Avala.Workbench.Triggers;
using Avala.Workbench.Usage;
using Avalonia.Styling;

namespace Avala.Workbench.Tests.Views;

public sealed class ReadProblemViewScripts(HeadlessUi ui)
{
    private const string Reason = "The usage store is locked";

    public static TheoryData<string> Followers => ["Usage", "Triggers", "Resources", "Archived", "Connections", "Delegation", "DelegationPane"];

    [Fact]
    public Task AReadProblemIsShownWithItsReasonAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignReadProblemViewModel(Reason));

            Assert.Equal((true, ReadProblemPhrases.Problem(Reason)), (view.Shows("Problem"), view.TextOf("Problem")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task WithoutAReadProblemNothingIsShownAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignReadProblemViewModel());

            Assert.False(view.Shows("Problem"));
        }, TestContext.Current.CancellationToken);

    [Theory]
    [MemberData(nameof(Followers))]
    public Task EveryPageAndPaneFollowingALiveFeedSaysWhenItCannotReadAsync(string follower) =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(Failing(follower, new DesignReadProblemViewModel(Reason)), ThemeVariant.Dark, 1000, 700);

            Assert.Equal((true, ReadProblemPhrases.Problem(Reason)), (view.Shows("ReadProblem") && view.Shows("Problem"), view.TextOf("Problem")));
        }, TestContext.Current.CancellationToken);

    private static object Failing(string follower, IReadProblemViewModel problem) =>
        follower switch
        {
            "Usage" => new DesignUsageViewModel { ReadProblem = problem },
            "Triggers" => new DesignTriggersViewModel { ReadProblem = problem },
            "Resources" => new DesignResourcesViewModel { ReadProblem = problem },
            "Archived" => new DesignArchivedViewModel { ReadProblem = problem },
            "Connections" => new DesignConnectionsViewModel { ReadProblem = problem },
            "Delegation" => new DesignDelegationViewModel { ReadProblem = problem },
            _ => new DesignDelegationPaneViewModel { ReadProblem = problem },
        };
}
