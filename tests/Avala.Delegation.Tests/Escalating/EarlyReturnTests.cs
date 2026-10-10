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

public sealed class EarlyReturnTests
{
    private static readonly DelegationRules AsksParent = Desk.Declared with { Escalation = new EscalationTerms(true, TimeSpan.FromSeconds(60)) };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AChildsRequestReturnsItsParentsPendingDelegateCallEarlyAndItsSiblingsCallsSayTheyStillRunAsync()
    {
        await using var desk = await StartedAsync();
        var asking = await desk.DelegateAsync("migrate", "Migrate the database");
        var sibling = await desk.DelegateAsync("seed", "Seed the database");

        await desk.Notes.HandleAsync(new PermissionDecided(Asked(desk, asking)), Cancellation);

        var returned = desk.Agents.Results.ToDictionary(result => result.Result.Item.Value, result => result.Result.Content);
        Assert.Contains("\"outcome\":\"asking\"", returned["migrate"], StringComparison.Ordinal);
        Assert.Contains($$"""
            "answer_child":{"child":"{{asking.Value}}","request":"run","decision":"allow"}
            """.Trim(), returned["migrate"], StringComparison.Ordinal);
        Assert.Contains("\"outcome\":\"running\"", returned["seed"], StringComparison.Ordinal);
        Assert.Contains($$"""
            "wait_child":{"child":"{{sibling.Value}}"}
            """.Trim(), returned["seed"], StringComparison.Ordinal);
        Assert.True(Assert.Single(desk.Bus.Published.OfType<ParentAsked>()).InCall);
        Assert.Empty(desk.Jobs.Steered);
    }

    [Fact]
    public async Task WaitingForAChildAgainTakesItsReportAsTheWaitCallsResultAsync()
    {
        await using var desk = await StartedAsync();
        var child = await desk.DelegateAsync("migrate", "Migrate the database");
        await desk.Notes.HandleAsync(new PermissionDecided(Asked(desk, child)), Cancellation);

        await desk.WaitChildAsync("wait", child);
        await desk.ReplyAsync(child, "Migrated.");
        await desk.ProgressAsync(child, JobStatus.AwaitingReview);
        var reported = await desk.ReportedAsync(child);

        var (_, result) = desk.Agents.Results[^1];
        Assert.Equal(new ItemId("wait"), result.Item);
        Assert.Contains("\"outcome\":\"integrated\"", result.Content, StringComparison.Ordinal);
        Assert.Equal(AnswerRoute.ToolResult, Outcomes.Present(reported.Answered).Route);
    }

    [Fact]
    public async Task AReportOfAChildWhoseCallReturnedEarlyWaitsForTheParentToWaitAgainAndIsItsResultOnceAsync()
    {
        await using var desk = await StartedAsync();
        var child = await desk.DelegateAsync("migrate", "Migrate the database");
        desk.Jobs.Steerable = true;
        await desk.Notes.HandleAsync(new PermissionDecided(Asked(desk, child)), Cancellation);
        await desk.ProgressAsync(child, JobStatus.Failed);
        _ = await desk.ReportedAsync(child);

        await desk.WaitChildAsync("wait", child);
        var waited = await desk.Agents.ResultOfAsync("wait", Cancellation);
        await desk.WaitChildAsync("again", child);
        var again = await desk.Agents.ResultOfAsync("again", Cancellation);

        var delivered = Assert.Single(desk.Bus.Published.OfType<ReportDelivered>());
        Assert.Equal(AnswerRoute.ToolResult, Outcomes.Present(delivered.Delegation.Answered).Route);
        Assert.Contains("\"outcome\":\"failed\"", waited.Content, StringComparison.Ordinal);
        Assert.Contains("already reached you", again.Content, StringComparison.Ordinal);
        Assert.Empty(desk.Jobs.Steered);
    }

    [Fact]
    public async Task WaitingForAJobThatIsNotOneOfYourChildrenIsRefusedAsync()
    {
        await using var desk = await StartedAsync();

        await desk.WaitChildAsync("wait", JobId.New());

        var result = Assert.Single(desk.Agents.Results).Result;
        Assert.True(result.IsError);
        Assert.Contains("notYourChild", result.Content, StringComparison.Ordinal);
    }

    private static async Task<Desk> StartedAsync()
    {
        var desk = new Desk(Option<DelegationRules>.Some(AsksParent));
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
