using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing.UI;
using Avala.Workbench.Cards;
using Avala.Workbench.Tests.Cards;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class AnswerScopeViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheScopeIsOneSegmentedControlWhoseChoiceIsHighlightedAndWhoseAlwaysNamesTheProgramsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(Scope(Asking.Offered("npm tee")));
            var before = (view.HasClass("Once", "selected"), view.HasClass("Always", "selected"));

            view.Click("Always");

            Assert.Equal((true, false), before);
            Assert.Equal((false, true, false), (view.HasClass("Once", "selected"), view.HasClass("Always", "selected"), view.HasClass("Everything", "selected")));
            Assert.Equal(("Once", "Always npm, tee in this job"), (view.Find<Button>("Once").Content, view.Find<Button>("Always").Content));
            Assert.Contains("Everything in this job", view.VisibleTexts);
            Assert.Equal((false, false, false), (view.Shows("AlwaysUnavailable"), view.Shows("EverythingUnavailable"), view.Shows("More")));
        }, Cancellation);

    [Fact]
    public Task AChoiceThatCannotBeMadeIsDisabledWithItsReasonInSightAndEverythingShowsALockAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(Scope(new JobScope(Option<PolicyRule>.None, ScopeRefusal.Unanalysable, Raisable: false)));

            view.Click("Always").Click("Everything");

            Assert.Equal((false, false, true), (view.Find<Button>("Always").IsEffectivelyEnabled, view.Find<Button>("Everything").IsEffectivelyEnabled, view.HasClass("Once", "selected")));
            Assert.Equal(("can't tell what this line runs", true, true), (view.TextOf("AlwaysUnavailable"), view.Shows("Lock"), view.Shows("EverythingUnavailable")));
            Assert.Contains("repository is supervised", view.VisibleTexts);
        }, Cancellation);

    [Fact]
    public Task AlwaysInThisRepositoryWaitsBehindTheMoreChoicesButtonAsync() =>
        ui.RunAsync(() =>
        {
            var repository = new PolicyRule(RuleOrigin.Repository, "always in this repository", ItemKind.Command, "dotnet test", RuleScope.Anywhere, PolicyAnswer.Allow);
            var scope = new AnswerScopeViewModel(ItemKind.Command, Decision(Asking.Offered("dotnet")) with { RepositoryRule = repository });
            var view = Screen.Show(scope);
            var hidden = view.Shows("InRepository");

            view.Click("More").Click("InRepository");

            Assert.Equal((false, true, true), (hidden, view.HasClass("InRepository", "selected"), scope.IsInRepository));
            Assert.Equal("Always in this repository", view.Find<Button>("InRepository").Content);
        }, Cancellation);

    private static AnswerScopeViewModel Scope(JobScope offered) => new(ItemKind.Command, Decision(offered));

    private static PolicyDecision Decision(JobScope offered) =>
        new(SessionId.New(), TurnId.New(), new ItemId("asked"), JobId.New(), ItemKind.Command, "target", PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToHuman, DateTimeOffset.UnixEpoch)
        {
            Scope = offered,
        };
}
