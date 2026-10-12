using Avala.Testing.UI;
using Avala.Workbench.Board;
using Avala.Workbench.Cards;
using Avala.Workbench.Tests.Cards;

namespace Avala.Workbench.Tests.Views;

public sealed class DeniedCardViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ADenialShowsWhatWasDeniedWithAnAllowThatTurnsIntoItsNoticeOnceClickedAsync() =>
        ui.RunAsync(async () =>
        {
            var denials = new FakeDeniedRequests();
            var card = new DeniedCardViewModel(new Denying().Entry(), Replying.With(new FakePermissionAnswers(), new FakeAgents(), denials, new FakeJobs(), new JobBoard()));
            var view = Screen.Show(card);
            var before = (view.TextOf("Verdict"), view.TextOf("What"), view.Shows("Allow"), view.Shows("Notice"));

            view.Click("Allow");
            await (card.AllowCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal(("Denied", "Fetch https://example.com/style", true, false), before);
            Assert.Single(denials.Allowed);
            Assert.Equal((false, true, false), (view.Shows("Allow"), view.Shows("Notice"), view.Shows("Why")));
        }, Cancellation);
}
