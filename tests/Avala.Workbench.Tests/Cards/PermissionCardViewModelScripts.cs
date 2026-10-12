using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Cards;

namespace Avala.Workbench.Tests.Cards;

public sealed class PermissionCardViewModelScripts
{
    private readonly Asking asking = new();
    private readonly FakePermissionAnswers permissions = new();

    [Fact]
    public void AWaitingRequestShowsWhatTheAgentWantsAndWaitsForYou() =>
        ViewModelScript.Given(Card())
            .Then(card =>
            {
                Assert.Equal(("Run the CheckoutForm tests", ItemKind.Command, "npm test -- CheckoutForm.test.tsx --runInBand"), (card.Title, card.Kind, card.Target));
                Assert.Equal(("Waiting for you", true, true), (card.Verdict, card.AwaitsYou, card.IsShown));
            });

    [Theory]
    [InlineData("waiting", "Waiting for its parent", true)]
    [InlineData("allowed", "Allowed by its parent", false)]
    [InlineData("denied", "Denied by its parent", false)]
    [InlineData("passed", "Waiting for you · its parent did not answer in time", true)]
    [InlineData("beyond", "Waiting for you · its parent's own rules do not allow it", true)]
    public void ARequestOfASubAgentSaysWhetherItWaitsForItsParentWhoAnsweredItOrWhyItCameToYou(string state, string verdict, bool awaitsYou) =>
        ViewModelScript.Given(new PermissionCardViewModel(
                asking.AskingParent(
                    Jobs.Contracts.JobId.New(),
                    state switch { "allowed" or "denied" => DecisionDelivery.Answered, "waiting" => DecisionDelivery.LeftToParent, _ => DecisionDelivery.LeftToHuman },
                    state switch { "passed" => PassReason.ParentTimedOut, "beyond" => PassReason.BeyondParent, _ => Option<PassReason>.None },
                    state switch { "allowed" => PermissionAnswer.Allow, "denied" => PermissionAnswer.Deny, _ => Option<PermissionAnswer>.None }),
                new(permissions, new FakeAgents())))
            .Then(card => Assert.Equal((verdict, awaitsYou, true), (card.Verdict, card.AwaitsYou, card.IsShown)));

    [Fact]
    public async Task AllowingSendsTheTrimmedNoteAndAlwaysTheProgramInThisJobToTheSessionOfTheRequest()
    {
        var card = Card();
        card.Note = " Only the checkout tests ";
        card.Scope.ChooseCommand.Execute(AnswerScope.Always);

        await card.AllowCommand.ExecuteAsync(null);

        var (answered, reply) = Assert.Single(permissions.Replies);
        Assert.Equal(
            (asking.Session, new ItemId("run"), PermissionAnswer.Allow, Option<string>.Some("Only the checkout tests"), Remember.ForThisJob),
            (answered, reply.Item, reply.Answer, reply.Message, reply.Remember));
        Assert.Equal((true, false, string.Empty), (card.Scope.IsAlways, card.Scope.IsOnce, card.Notice));
    }

    [Theory]
    [InlineData(ScopeRefusal.Unanalysable, "can't tell what this line runs")]
    [InlineData(ScopeRefusal.StillAsked, "a rule still asks for this")]
    public async Task ALineThatCannotBeRememberedForItsProgramSaysWhyAndIsAnsweredOnce(ScopeRefusal refusal, string reason)
    {
        var card = new PermissionCardViewModel(asking.Refused("echo $(date)", refusal), new(permissions, new FakeAgents()));
        var offered = card.Scope.ChooseCommand.CanExecute(AnswerScope.Always);

        await card.AllowCommand.ExecuteAsync(null);

        Assert.Equal((false, "Always this program in this job", reason), (offered, card.Scope.AlwaysLabel, card.Scope.AlwaysUnavailable));
        Assert.Equal(Remember.Once, Assert.Single(permissions.Replies).Reply.Remember);
    }

    [Fact]
    public async Task EverythingInThisJobIsSentAndTheCardSaysTheRestOfTheJobRunsWithoutAsking()
    {
        var card = Card();
        card.Scope.ChooseCommand.Execute(AnswerScope.Everything);

        await card.AllowCommand.ExecuteAsync(null);

        Assert.Equal(Remember.EverythingInThisJob, Assert.Single(permissions.Replies).Reply.Remember);
        Assert.Equal("The rest of this job runs without asking, within the repository's guards.", card.Notice);
    }

    [Theory]
    [InlineData(false, Remember.ForThisContest)]
    [InlineData(true, Remember.EverythingInThisContest)]
    public async Task ARequestOfAContestCandidateAppliesTheChosenScopeToEveryCandidate(bool everything, Remember remember)
    {
        var card = new PermissionCardViewModel(asking.Contested(), new(permissions, new FakeAgents()));
        card.Scope.ChooseCommand.Execute(everything ? AnswerScope.Everything : AnswerScope.Always);

        await card.AllowCommand.ExecuteAsync(null);

        Assert.Equal(remember, Assert.Single(permissions.Replies).Reply.Remember);
        Assert.Equal(("Always dotnet in this contest", "Everything in this contest"), (card.Scope.AlwaysLabel, card.Scope.EverythingLabel));
    }

    [Theory]
    [InlineData(null, "Added to .avala/permissions.json. It applies to new jobs once you commit it; this job won't ask again.")]
    [InlineData(PolicyError.Malformed, "Not added to .avala/permissions.json: the repository's file is not valid; fix it in Settings first. This job won't ask again.")]
    [InlineData(PolicyError.RepositoryUnwritable, "Not added to .avala/permissions.json: the repository's file could not be written. This job won't ask again.")]
    public async Task AlwaysInThisRepositoryIsSentAndTheCardSaysWhetherTheRuleWasAdded(PolicyError? refusal, string notice)
    {
        permissions.RepositoryRefusal = refusal.ToOption();
        var card = new PermissionCardViewModel(asking.OfferingTheRepository(), new(permissions, new FakeAgents()));
        card.Scope.ChooseCommand.Execute(AnswerScope.InRepository);

        await card.AllowCommand.ExecuteAsync(null);

        Assert.Equal((Remember.InThisRepository, true), (Assert.Single(permissions.Replies).Reply.Remember, card.Scope.IsInRepository));
        Assert.Equal(notice, card.Notice);
    }

    [Theory]
    [InlineData("added", "Added to .avala/permissions.json. It applies to new jobs once you commit it; this job won't ask again.")]
    [InlineData("refused", "Not added to .avala/permissions.json: the repository's file could not be written. This job won't ask again.")]
    [InlineData("once", "")]
    public void ACardSaysWhetherTheRepositoryRuleWasAddedWhereverItsRequestWasAnswered(string answered, string notice)
    {
        var request = asking.OfferingTheRepository("dotnet test");
        var answer = new HumanAnswer(request.Session, Option<Jobs.Contracts.JobId>.None, request.Item, request.Kind, request.Target, PermissionAnswer.Allow, Option<string>.None, Option<PolicyRule>.None, DateTimeOffset.UnixEpoch)
        {
            RepositoryRule = answered == "added" ? request.Decision.Bind(decision => decision.RepositoryRule) : Option<PolicyRule>.None,
            RepositoryError = answered == "refused" ? PolicyError.RepositoryUnwritable : Option<PolicyError>.None,
        };

        ViewModelScript.Given(new PermissionCardViewModel(request, new(permissions, new FakeAgents())))
            .When(card => card.Update(request with { Resolution = PermissionAnswer.Allow, Answered = answer }))
            .Then(card => Assert.Equal((notice, 0), (card.Notice, permissions.Replies.Count)));
    }

    [Fact]
    public async Task DenyingWithoutANoteSendsNoMessage()
    {
        var card = Card();
        card.Note = "   ";

        await card.DenyCommand.ExecuteAsync(null);

        var reply = Assert.Single(permissions.Replies).Reply;
        Assert.Equal((PermissionAnswer.Deny, Option<string>.None), (reply.Answer, reply.Message));
    }

    [Fact]
    public async Task AnAnswerTheAgentNoLongerWaitsForIsReportedOnTheCard()
    {
        permissions.Refuse = true;
        var card = Card();

        await card.DenyCommand.ExecuteAsync(null);

        Assert.Equal("The agent no longer waits for this answer.", card.Error);
    }

    [Fact]
    public void AResolvedRequestCanNoLongerBeAnsweredAndSaysHow() =>
        ViewModelScript.Given(Card())
            .When(card => card.Update(asking.Permission() with { Resolution = PermissionAnswer.Allow }))
            .ThenNotified(nameof(PermissionCardViewModel.AwaitsYou), nameof(PermissionCardViewModel.Verdict))
            .Then(card => Assert.Equal((false, false, "Allowed"), (card.AllowCommand.CanExecute(null), card.DenyCommand.CanExecute(null), card.Verdict)));

    [Fact]
    public void ARequestWhoseTurnEndedIsNoLongerWaiting() =>
        ViewModelScript.Given(Card())
            .When(card => card.Update(asking.Permission() with { Closed = true }))
            .Then(card => Assert.Equal((false, "No longer waiting"), (card.AwaitsYou, card.Verdict)));

    [Fact]
    public void ARequestThePolicyAnsweredItselfIsNotShownInTheConversation() =>
        ViewModelScript.Given(new PermissionCardViewModel(asking.Permission(delivery: DecisionDelivery.Answered), new(permissions, new FakeAgents())))
            .Then(card => Assert.Equal((false, false), (card.IsShown, card.AwaitsYou)));

    [Fact]
    public async Task WhileAnAnswerIsInFlightNeitherAnswerCanBeGivenAgain()
    {
        var gate = new TaskCompletionSource();
        var card = new PermissionCardViewModel(asking.Permission(), new(new GatedAnswers(gate.Task, permissions), new FakeAgents()));

        var first = card.AllowCommand.ExecuteAsync(null);
        var again = (card.AllowCommand.CanExecute(null), card.DenyCommand.CanExecute(null));
        gate.SetResult();
        await first;

        Assert.Equal((false, false), again);
        Assert.Equal(PermissionAnswer.Allow, Assert.Single(permissions.Replies).Reply.Answer);
    }

    private PermissionCardViewModel Card() => new(asking.Permission(), new(permissions, new FakeAgents()));

    private sealed class GatedAnswers(Task gate, FakePermissionAnswers inner) : IPermissionAnswers
    {
        public async ValueTask<Result<HumanAnswer, PolicyError>> AnswerAsync(SessionId session, PermissionReply reply, CancellationToken cancellationToken)
        {
            await gate;

            return await inner.AnswerAsync(session, reply, cancellationToken);
        }
    }
}
