using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Cards;
using Avala.Workbench.Timeline;

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

    [Fact]
    public void AFolderRequestShowsTheFolderAndWhyAndOffersReadOnlyBesideReadAndEdit() =>
        ViewModelScript.Given(new PermissionCardViewModel(asking.FolderCard(FolderAccess.Edit), Replying.With(permissions)))
            .Then(card =>
            {
                Assert.Equal(
                    ("Wants to read and edit a folder outside its worktree", "Bump the shared contracts package", Asking.Contracts),
                    (card.Headline, card.Title, card.Target));
                Assert.Equal((true, true, "Read and edit", "Waiting for you"), (card.IsFolder, card.OffersReadOnly, card.AllowLabel, card.Verdict));
            });

    [Fact]
    public void AFolderRequestToReadOffersOnlyReading() =>
        ViewModelScript.Given(new PermissionCardViewModel(asking.FolderCard(FolderAccess.Read), Replying.With(permissions)))
            .Then(card => Assert.Equal(("Wants to read a folder outside its worktree", false, "Allow reading"), (card.Headline, card.OffersReadOnly, card.AllowLabel)));

    [Fact]
    public async Task ReadAndEditAndReadOnlyGrantTheFolderForTheRestOfTheJob()
    {
        var card = new PermissionCardViewModel(asking.FolderCard(FolderAccess.Edit), Replying.With(permissions));

        await card.AllowCommand.ExecuteAsync(null);
        await card.ReadOnlyCommand.ExecuteAsync(null);
        await card.DenyCommand.ExecuteAsync(null);

        Assert.Equal(
            [(PermissionAnswer.Allow, Remember.EditsInThisFolder), (PermissionAnswer.Allow, Remember.ReadsInThisFolder), (PermissionAnswer.Deny, Remember.Once)],
            permissions.Replies.Select(replied => (replied.Reply.Answer, replied.Reply.Remember)));
    }

    [Theory]
    [InlineData(FolderAccess.Edit, "Granted read and edit")]
    [InlineData(FolderAccess.Read, "Granted read only")]
    public void AnAnsweredFolderRequestSaysWhatWasGrantedAndForHowLong(FolderAccess access, string verdict)
    {
        var granted = Assert.Single(asking.Folder(FolderAccess.Edit)
            .Apply(new HumanAnswer(asking.Session, Option<Jobs.Contracts.JobId>.None, new ItemId("folder"), ItemKind.Other, Asking.Contracts, PermissionAnswer.Allow, Option<string>.None, Asking.Grant(access), DateTimeOffset.UnixEpoch))
            .Entries.OfType<PermissionEntry>());

        ViewModelScript.Given(new PermissionCardViewModel(granted, Replying.With(permissions)))
            .Then(card =>
            {
                Assert.Equal((verdict, false), (card.Verdict, card.AwaitsYou));
                Assert.StartsWith($"For the rest of this job, reads {(access == FolderAccess.Edit ? "and edits " : string.Empty)}in {Asking.Contracts} no longer ask.", card.Notice, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task AnEditOutsideTheWorktreeOffersItsRepositoryForTheRestOfTheJob()
    {
        var card = new PermissionCardViewModel(asking.OutsideEdit(), Replying.With(permissions));
        card.Scope.ChooseCommand.Execute(AnswerScope.Always);

        await card.AllowCommand.ExecuteAsync(null);

        Assert.Equal(("Always in contracts for this job", string.Empty, false), (card.Scope.AlwaysLabel, card.Scope.AlwaysUnavailable, card.IsFolder));
        Assert.Equal(Remember.EditsInThisFolder, Assert.Single(permissions.Replies).Reply.Remember);
    }

    [Theory]
    [InlineData("waiting", "Waiting for its parent", true)]
    [InlineData("allowed", "Allowed by its parent", false)]
    [InlineData("denied", "Denied by its parent", false)]
    public void ARequestOfASubAgentRecordedBeforeItsPermissionsWentToYouSaysWhetherItWaitsForItsParentOrWhoAnsweredIt(string state, string verdict, bool awaitsYou) =>
        ViewModelScript.Given(new PermissionCardViewModel(
                asking.AskingParent(
                    Jobs.Contracts.JobId.New(),
                    state == "waiting" ? DecisionDelivery.LeftToParent : DecisionDelivery.Answered,
                    state switch { "allowed" => PermissionAnswer.Allow, "denied" => PermissionAnswer.Deny, _ => Option<PermissionAnswer>.None }),
                Replying.With(permissions)))
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
        var card = new PermissionCardViewModel(asking.Refused("echo $(date)", refusal), Replying.With(permissions));
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
        var card = new PermissionCardViewModel(asking.Contested(), Replying.With(permissions));
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
        var card = new PermissionCardViewModel(asking.OfferingTheRepository(), Replying.With(permissions));
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

        ViewModelScript.Given(new PermissionCardViewModel(request, Replying.With(permissions)))
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
        ViewModelScript.Given(new PermissionCardViewModel(asking.Permission(delivery: DecisionDelivery.Answered), Replying.With(permissions)))
            .Then(card => Assert.Equal((false, false), (card.IsShown, card.AwaitsYou)));

    [Fact]
    public async Task WhileAnAnswerIsInFlightNeitherAnswerCanBeGivenAgain()
    {
        var gate = new TaskCompletionSource();
        var card = new PermissionCardViewModel(asking.Permission(), Replying.With(new GatedAnswers(gate.Task, permissions)));

        var first = card.AllowCommand.ExecuteAsync(null);
        var again = (card.AllowCommand.CanExecute(null), card.DenyCommand.CanExecute(null));
        gate.SetResult();
        await first;

        Assert.Equal((false, false), again);
        Assert.Equal(PermissionAnswer.Allow, Assert.Single(permissions.Replies).Reply.Answer);
    }

    private PermissionCardViewModel Card() => new(asking.Permission(), Replying.With(permissions));

    private sealed class GatedAnswers(Task gate, FakePermissionAnswers inner) : IPermissionAnswers
    {
        public async ValueTask<Result<HumanAnswer, PolicyError>> AnswerAsync(SessionId session, PermissionReply reply, CancellationToken cancellationToken)
        {
            await gate;

            return await inner.AnswerAsync(session, reply, cancellationToken);
        }
    }
}
