using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Cards;

namespace Avala.Workbench.Tests.Cards;

public sealed class AnswerScopeViewModelScripts
{
    [Fact]
    public void TheScopeStartsAtOnceAndOffersAlwaysTheProgramAndEverythingInThisJob() =>
        ViewModelScript.Given(Scope(ItemKind.Command, Asking.Offered("npm")))
            .Then(scope => Assert.Equal(
                (true, Remember.Once, "Always npm in this job", "Everything in this job", string.Empty, string.Empty),
                (scope.IsOnce, scope.Remember, scope.AlwaysLabel, scope.EverythingLabel, scope.AlwaysUnavailable, scope.EverythingUnavailable)));

    [Fact]
    public void ChoosingAScopeHighlightsOnlyThatChoice() =>
        ViewModelScript.Given(Scope(ItemKind.Command, Asking.Offered("npm")))
            .When(scope => scope.ChooseCommand.Execute(AnswerScope.Everything))
            .ThenNotified(nameof(AnswerScopeViewModel.IsOnce), nameof(AnswerScopeViewModel.IsEverything))
            .Then(scope => Assert.Equal((false, false, true, Remember.EverythingInThisJob), (scope.IsOnce, scope.IsAlways, scope.IsEverything, scope.Remember)));

    [Fact]
    public void AChainedLineNamesEveryProgramItsAlwaysChoiceWouldRemember() =>
        ViewModelScript.Given(Scope(ItemKind.Command, Asking.Offered("xargs touch")))
            .Then(scope => Assert.Equal("Always xargs, touch in this job", scope.AlwaysLabel));

    [Theory]
    [InlineData(ItemKind.Web, "example.com", "Always example.com in this job")]
    [InlineData(ItemKind.Mcp, "deploy_preview", "Always deploy_preview in this job")]
    public void AlwaysNamesTheHostOrTheToolOfARequestThatIsNotACommand(ItemKind kind, string subject, string label) =>
        ViewModelScript.Given(Scope(kind, new JobScope(new PolicyRule(RuleOrigin.Job, "always", kind, subject, RuleScope.Anywhere, PolicyAnswer.Allow) { AnyArguments = true }, Option<ScopeRefusal>.None, Raisable: true)))
            .Then(scope => Assert.Equal(label, scope.AlwaysLabel));

    [Theory]
    [InlineData(ItemKind.Command, ScopeRefusal.Unanalysable, "Always this program in this job", "can't tell what this line runs")]
    [InlineData(ItemKind.Web, ScopeRefusal.NoSubject, "Always this host in this job", "no single host to remember")]
    [InlineData(ItemKind.FileEdit, ScopeRefusal.StillAsked, "Always this file in this job", "a rule still asks for this")]
    public void AnAlwaysThatCannotBeRememberedIsUnavailableWithItsReasonAndAnswersOnce(ItemKind kind, ScopeRefusal refusal, string label, string reason) =>
        ViewModelScript.Given(Scope(kind, new JobScope(Option<PolicyRule>.None, refusal, Raisable: true)))
            .When(scope => scope.ChooseCommand.Execute(AnswerScope.Always))
            .Then(scope => Assert.Equal(
                (false, label, reason, Remember.Once),
                (scope.ChooseCommand.CanExecute(AnswerScope.Always), scope.AlwaysLabel, scope.AlwaysUnavailable, scope.Remember)));

    [Fact]
    public void ARepositoryThatDeclaresSupervisedLocksEverythingInThisJobAndSaysSo() =>
        ViewModelScript.Given(Scope(ItemKind.Command, Asking.Offered("npm", raisable: false)))
            .Then(scope => Assert.Equal((false, "repository is supervised"), (scope.ChooseCommand.CanExecute(AnswerScope.Everything), scope.EverythingUnavailable)));

    [Fact]
    public void AlwaysInThisRepositoryIsOfferedOnlyWhenThePolicyFoundAnExactRuleForTheRequest()
    {
        var repository = new PolicyRule(RuleOrigin.Repository, "always in this repository", ItemKind.Command, "dotnet test", RuleScope.Anywhere, PolicyAnswer.Allow);

        ViewModelScript.Given(new AnswerScopeViewModel(ItemKind.Command, Decision(ItemKind.Command, Asking.Offered("dotnet")) with { RepositoryRule = repository }))
            .Then(scope =>
            {
                Assert.Equal((true, true, "Always in this repository"), (scope.OffersInRepository, scope.ChooseCommand.CanExecute(AnswerScope.InRepository), scope.InRepositoryLabel));
                Assert.Equal(
                    "Adds a rule for exactly dotnet test to .avala/permissions.json in the repository. It applies to new jobs once committed; this job stops asking now.",
                    scope.InRepositoryHint);
                Assert.False(Scope(ItemKind.Command, Asking.Offered("dotnet")).ChooseCommand.CanExecute(AnswerScope.InRepository));
            });
    }

    [Fact]
    public void AScopeLearnsWhatItOffersOnceTheDecisionArrives() =>
        ViewModelScript.Given(new AnswerScopeViewModel(ItemKind.Command, Option<PolicyDecision>.None))
            .When(scope => scope.Update(ItemKind.Command, Decision(ItemKind.Command, Asking.Offered("make"))))
            .ThenNotified(nameof(AnswerScopeViewModel.AlwaysLabel))
            .Then(scope => Assert.Equal((true, "Always make in this job"), (scope.ChooseCommand.CanExecute(AnswerScope.Always), scope.AlwaysLabel)));

    private static AnswerScopeViewModel Scope(ItemKind kind, JobScope offered) => new(kind, Decision(kind, offered));

    private static PolicyDecision Decision(ItemKind kind, JobScope offered) =>
        new(SessionId.New(), TurnId.New(), new ItemId("asked"), JobId.New(), kind, "target", PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToHuman, DateTimeOffset.UnixEpoch)
        {
            Scope = offered,
        };
}
