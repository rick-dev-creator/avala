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

    [Fact]
    public async Task AllowingSendsTheTrimmedNoteAndDontAskAgainToTheSessionOfTheRequest()
    {
        var card = Card();
        card.Note = " Only the checkout tests ";
        card.DontAskAgain = true;

        await card.AllowCommand.ExecuteAsync(null);

        var (answered, reply) = Assert.Single(permissions.Replies);
        Assert.Equal(
            (asking.Session, new ItemId("run"), PermissionAnswer.Allow, Option<string>.Some("Only the checkout tests"), true),
            (answered, reply.Item, reply.Answer, reply.Message, reply.DontAskAgain));
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
