using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Delegation.Tests.Delegating;

public sealed class RestartedDeskTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AReportTheParentsCallCannotReceiveStaysOwedUntilTheParentIsBriefedExactlyOnceAsync()
    {
        await using var desk = new Desk();
        await desk.StartedAsync();
        var child = await desk.DelegateAsync("notes");
        desk.Agents.Closed = true;

        await desk.ProgressAsync(child, JobStatus.Failed);
        var reported = await desk.ReportedAsync(child);
        var briefing = Outcomes.Present(await desk.Briefing.BriefAsync(desk.Parent, Cancellation));

        Assert.True(reported.Answered.IsNone);
        Assert.Contains($"delegate call notes: {{\"job\":\"{child.Value}\",\"outcome\":\"failed\"", briefing, StringComparison.Ordinal);
        var delivered = Assert.Single(desk.Bus.Published.OfType<ReportDelivered>()).Delegation;
        Assert.Equal((Option<JobId>.Some(child), AnswerRoute.Message), (delivered.Child, Outcomes.Present(delivered.Answered).Route));
        Assert.Equal(delivered, Outcomes.Present(desk.Book.OfChild(child)));
        Assert.True((await desk.Briefing.BriefAsync(desk.Parent, Cancellation)).IsNone);
    }

    [Fact]
    public async Task AChildStillRunningAtTheRestartIsReportedToItsParentWhenItSettlesAsync()
    {
        await using var desk = new Desk();
        var child = Earlier(desk, "notes", out var pending);
        desk.Store.Earlier = [pending];
        await desk.Book.RunAsync(Cancellation);

        await desk.ReplyAsync(child, "Wrote ", "NOTES.md.");
        await desk.ProgressAsync(child, JobStatus.AwaitingReview);

        var report = Outcomes.Present((await desk.ReportedAsync(child)).Report);
        Assert.Equal((ChildOutcome.Integrated, Option<string>.Some("Wrote NOTES.md.")), (report.Outcome, report.Summary));
    }

    [Fact]
    public async Task AChildThatSettledBeforeStartupCompletedIsIntegratedAndReportedOnlyOnceAsync()
    {
        await using var desk = new Desk();
        var settled = Earlier(desk, "notes", out var first);
        var later = Earlier(desk, "todo", out var second);
        desk.Store.Earlier = [first, second];
        await desk.Book.RunAsync(Cancellation);
        desk.Jobs.Is(settled, JobStatus.AwaitingReview);

        await desk.StartupCompletedAsync();
        var report = Outcomes.Present((await desk.ReportedAsync(settled)).Report);
        await desk.ProgressAsync(settled, JobStatus.AwaitingReview);
        await desk.ProgressAsync(later, JobStatus.Failed);
        _ = await desk.ReportedAsync(later);

        Assert.Equal(ChildOutcome.Integrated, report.Outcome);
        Assert.Single(desk.Bus.Published.OfType<ChildReported>(), reported => reported.Delegation.Child == Option<JobId>.Some(settled));
    }

    [Fact]
    public async Task RecoveryDefersOnlyAParentStillOwedTheResultOfACallAsync()
    {
        await using var desk = new Desk();
        _ = Earlier(desk, "notes", out var owed);
        var answered = JobId.New();
        _ = Earlier(desk, "todo", out var told);
        desk.Store.Earlier = [owed, told with { Parent = answered, Answered = new CallAnswer(AnswerRoute.ToolResult, desk.Clock.GetUtcNow()) }];

        Assert.True(await desk.Resumption.DefersAsync(desk.Parent, Cancellation));
        Assert.False(await desk.Resumption.DefersAsync(answered, Cancellation));
        Assert.False(await desk.Resumption.DefersAsync(JobId.New(), Cancellation));
    }

    [Fact]
    public async Task ADeferredParentIsResumedOnceWhenTheLastChildItWaitsForReportsAsync()
    {
        await using var desk = new Desk { Agents = { Closed = true } };
        var first = Earlier(desk, "notes", out var notes);
        var second = Earlier(desk, "todo", out var todo);
        desk.Store.Earlier = [notes, todo];
        Assert.True(await desk.Resumption.DefersAsync(desk.Parent, Cancellation));
        await desk.StartupCompletedAsync();

        await desk.ProgressAsync(first, JobStatus.Failed);
        _ = await desk.ReportedToResumptionAsync(first);
        var waiting = desk.Jobs.Resumed.Count;
        await desk.ProgressAsync(second, JobStatus.Failed);
        var last = await desk.ReportedToResumptionAsync(second);
        await desk.Resumption.HandleAsync(new ChildReported(last), Cancellation);

        Assert.Equal(0, waiting);
        Assert.Equal([desk.Parent], desk.Jobs.Resumed);
    }

    [Fact]
    public async Task ADeferredParentWhoseChildrenAllReportedBeforeTheRestartIsResumedWhenStartupCompletesAsync()
    {
        await using var desk = new Desk();
        var child = Earlier(desk, "notes", out var pending);
        desk.Store.Earlier = [pending with { Report = new ChildReport(child, ChildOutcome.Failed, JobStatus.Failed, desk.Clock.GetUtcNow()) }];
        Assert.True(await desk.Resumption.DefersAsync(desk.Parent, Cancellation));

        await desk.StartupCompletedAsync();

        Assert.Equal([desk.Parent], desk.Jobs.Resumed);
        Assert.Empty(desk.Bus.Published.OfType<ChildReported>());
    }

    private static JobId Earlier(Desk desk, string item, out DelegationRecord record)
    {
        var child = desk.Jobs.Running(desk.Parent);
        record = new DelegationRecord(desk.Session, new ItemId(item), "Write the release notes", desk.Clock.GetUtcNow())
        {
            Parent = desk.Parent,
            Depth = 1,
            Child = child,
            Connection = new ConnectionName("work"),
            Autonomy = Autonomy.Supervised,
        };

        return child;
    }
}
