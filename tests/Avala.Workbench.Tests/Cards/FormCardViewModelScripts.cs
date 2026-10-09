using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Cards;

namespace Avala.Workbench.Tests.Cards;

public sealed class FormCardViewModelScripts
{
    private readonly Asking asking = new();
    private readonly FakeAgents agents = new();

    [Fact]
    public void AWaitingFormShowsItsQuestionWithTheRecommendedOptionChosen() =>
        ViewModelScript.Given(Card())
            .Then(card =>
            {
                Assert.Equal((FormPurpose.Question, "Add invoice PDF endpoint", "The service stores invoices.", "Waiting for you"), (card.Purpose, card.Title, card.Context, card.Verdict));
                Assert.Equal([true, false], Assert.Single(card.Fields).Choices.Select(choice => choice.IsSelected));
                Assert.True(card.SubmitCommand.CanExecute(null));
            });

    [Fact]
    public async Task AFormIsAnsweredWithItsRecommendedOptionsByDefault()
    {
        await Card().SubmitCommand.ExecuteAsync(null);

        var (answered, answer) = Assert.Single(agents.Answers);
        Assert.Equal(asking.Session, answered);
        Assert.Equal(["PostgreSQL"], Assert.Single(answer.Fields).Chosen);
    }

    [Fact]
    public void AnIncompleteFormCannotBeSubmittedUntilItsFieldIsChosen()
    {
        var card = new FormCardViewModel(asking.Form(Asking.Choice(new FormOption("PostgreSQL", "Relational"), new FormOption("SQLite", "A file"))), new(new FakePermissionAnswers(), agents));
        var before = card.SubmitCommand.CanExecute(null);

        card.Fields[0].Choices[1].IsSelected = true;

        Assert.Equal((false, true), (before, card.SubmitCommand.CanExecute(null)));
    }

    [Fact]
    public async Task DecliningAFormTellsTheAgentWhy()
    {
        var card = Card();
        card.Note = "Ask the team first";

        await card.DeclineCommand.ExecuteAsync(null);

        var answer = Assert.Single(agents.Answers).Answer;
        Assert.Equal((true, Option<string>.Some("Ask the team first")), (answer.Declined, answer.Message));
    }

    [Fact]
    public async Task AnAnswerTheAgentRefusedIsReportedOnTheCard()
    {
        var card = new FormCardViewModel(asking.Form(), new(new FakePermissionAnswers(), new RefusingAgents()));

        await card.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("The agent no longer waits for this form.", card.Error);
    }

    [Fact]
    public void AnAnsweredFormSaysWhatWasChosenAndTakesNoMoreAnswers() =>
        ViewModelScript.Given(Card())
            .When(card => card.Update(asking.Form() with { Answer = new FormAnswer(new ItemId("question"), [new FieldAnswer("database") { Chosen = ["PostgreSQL"] }]) }))
            .ThenNotified(nameof(FormCardViewModel.AwaitsYou), nameof(FormCardViewModel.Verdict))
            .Then(card => Assert.Equal(("Answered: PostgreSQL", false, false), (card.Verdict, card.SubmitCommand.CanExecute(null), card.DeclineCommand.CanExecute(null))));

    [Fact]
    public async Task WhileAnAnswerIsInFlightNeitherSubmitNorDeclineCanRunAgain()
    {
        var gate = new TaskCompletionSource();
        var card = new FormCardViewModel(asking.Form(), new(new FakePermissionAnswers(), new GatedAgents(gate.Task, agents)));

        var first = card.SubmitCommand.ExecuteAsync(null);
        var again = (card.SubmitCommand.CanExecute(null), card.DeclineCommand.CanExecute(null));
        gate.SetResult();
        await first;

        Assert.Equal((false, false), again);
        Assert.False(Assert.Single(agents.Answers).Answer.Declined);
    }

    private FormCardViewModel Card() => new(asking.Form(), new(new FakePermissionAnswers(), agents));

    private sealed class RefusingAgents : IAgents
    {
        private readonly FakeAgents inner = new();

        public ValueTask<Result<OpenedSession, AgentError>> OpenAsync(AgentRequest request, CancellationToken cancellationToken) => inner.OpenAsync(request, cancellationToken);

        public bool IsOpen(SessionId session) => true;

        public ValueTask<Result<AgentTurn, AgentError>> SendAsync(SessionId session, string message, CancellationToken cancellationToken) => inner.SendAsync(session, message, cancellationToken);

        public ValueTask<Result<ItemId, AgentError>> RespondAsync(SessionId session, PermissionDecision decision, CancellationToken cancellationToken) => inner.RespondAsync(session, decision, cancellationToken);

        public ValueTask<Result<ItemId, AgentError>> AnswerAsync(SessionId session, FormAnswer answer, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<ItemId, AgentError>.Failure(AgentError.NoPendingForm));

        public ValueTask<Result<ItemId, AgentError>> ReturnAsync(SessionId session, ToolResult result, CancellationToken cancellationToken) => inner.ReturnAsync(session, result, cancellationToken);

        public ValueTask<Result<TurnId, AgentError>> InterruptAsync(SessionId session, CancellationToken cancellationToken) => inner.InterruptAsync(session, cancellationToken);

        public ValueTask<Result<AgentTurn, AgentError>> SteerAsync(SessionId session, string message, CancellationToken cancellationToken) => inner.SteerAsync(session, message, cancellationToken);

        public ValueTask<Result<SessionId, AgentError>> StopAsync(SessionId session, CancellationToken cancellationToken) => inner.StopAsync(session, cancellationToken);
    }

    private sealed class GatedAgents(Task gate, FakeAgents inner) : IAgents
    {
        public ValueTask<Result<OpenedSession, AgentError>> OpenAsync(AgentRequest request, CancellationToken cancellationToken) => inner.OpenAsync(request, cancellationToken);

        public bool IsOpen(SessionId session) => true;

        public ValueTask<Result<AgentTurn, AgentError>> SendAsync(SessionId session, string message, CancellationToken cancellationToken) => inner.SendAsync(session, message, cancellationToken);

        public ValueTask<Result<ItemId, AgentError>> RespondAsync(SessionId session, PermissionDecision decision, CancellationToken cancellationToken) => inner.RespondAsync(session, decision, cancellationToken);

        public async ValueTask<Result<ItemId, AgentError>> AnswerAsync(SessionId session, FormAnswer answer, CancellationToken cancellationToken)
        {
            await gate;

            return await inner.AnswerAsync(session, answer, cancellationToken);
        }

        public ValueTask<Result<ItemId, AgentError>> ReturnAsync(SessionId session, ToolResult result, CancellationToken cancellationToken) => inner.ReturnAsync(session, result, cancellationToken);

        public ValueTask<Result<TurnId, AgentError>> InterruptAsync(SessionId session, CancellationToken cancellationToken) => inner.InterruptAsync(session, cancellationToken);

        public ValueTask<Result<AgentTurn, AgentError>> SteerAsync(SessionId session, string message, CancellationToken cancellationToken) => inner.SteerAsync(session, message, cancellationToken);

        public ValueTask<Result<SessionId, AgentError>> StopAsync(SessionId session, CancellationToken cancellationToken) => inner.StopAsync(session, cancellationToken);
    }
}
