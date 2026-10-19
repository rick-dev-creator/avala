using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Delegation.Policy;
using Avala.Delegation.Tests.Delegating;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Delegation.Tests.Escalating;

public sealed class StopChildTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AStoppedChildIsDiscardedAndItsStoppedReportIsTheStopCallsResultOnceAsync()
    {
        await using var desk = await StartedAsync();
        var child = await desk.DelegateAsync("migrate", "Migrate the database");
        await desk.Notes.HandleAsync(new PermissionDecided(Asked(desk, child)), Cancellation);

        await desk.StopChildAsync("stop", child);
        await desk.ProgressAsync(child, JobStatus.Discarded);
        var reported = await desk.ReportedAsync(child);

        Assert.Equal([child], desk.Jobs.Discarded);

        var stopped = await desk.Agents.ResultOfAsync("stop", Cancellation);
        Assert.Contains("\"state\":\"stopped\"", stopped.Content, StringComparison.Ordinal);
        Assert.Contains("\"outcome\":\"discarded\"", stopped.Content, StringComparison.Ordinal);
        Assert.Equal(AnswerRoute.ToolResult, Outcomes.Present(reported.Answered).Route);
        Assert.Single(desk.Bus.Published.OfType<ReportDelivered>());
    }

    [Theory]
    [InlineData("not yours", "notYourChild")]
    [InlineData("ended", "notRunning")]
    public async Task StoppingAJobThatIsNotYourChildOrHasEndedIsRefusedAsync(string situation, string refusal)
    {
        await using var desk = await StartedAsync();
        var child = await desk.DelegateAsync("migrate", "Migrate the database");
        await desk.ProgressAsync(child, JobStatus.Failed);
        _ = await desk.ReportedAsync(child);
        desk.Jobs.Is(child, JobStatus.Failed);

        await desk.StopChildAsync("stop", situation == "ended" ? child : JobId.New());

        var result = await desk.Agents.ResultOfAsync("stop", Cancellation);
        Assert.True(result.IsError);
        Assert.Contains(refusal, result.Content, StringComparison.Ordinal);
    }

    private static async Task<Desk> StartedAsync()
    {
        var desk = new Desk(Option<DelegationRules>.Some(Desk.Declared with { Escalation = new EscalationTerms(true, TimeSpan.FromSeconds(60)) }));
        await desk.StartedAsync();
        desk.Audit.Working[desk.Session] = desk.Parent;

        return desk;
    }

    private static PolicyDecision Asked(Desk desk, JobId child) =>
        new(SessionId.New(), TurnId.New(), new ItemId("run"), child, ItemKind.Command, "dotnet ef database update", PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToParent, desk.Clock.GetUtcNow())
        {
            Parent = desk.Parent,
        };
}
