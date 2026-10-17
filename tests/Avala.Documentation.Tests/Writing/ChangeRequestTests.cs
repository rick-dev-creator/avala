using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using static Avala.Documentation.Tests.Writing.Documented;

namespace Avala.Documentation.Tests.Writing;

public sealed class ChangeRequestTests
{
    private const string Folder = "docs/features/handle-stripe-refund-webhooks";

    [Fact]
    public async Task ASteerIsRecordedAsAChangeRequestThatTheNextVersionExplainsAndLinksInChangesAsync()
    {
        var documented = Documented.With();
        var (job, session) = await documented.RunningAsync();
        await documented.PlanAsync(session, "Add a Webhooks container");
        await KeepAsync(documented, "# v1\n");

        await SteerAsync(documented, session, "  Make sure a refund never applies twice  ");
        await documented.PlanAsync(session, "Add a Webhooks container", "Keep idempotency keys");
        var brief = documented.Jobs.Submitted[^1].Request.Instruction;
        await KeepAsync(documented, "# v2\n");

        var request = Assert.Single(Outcomes.Present(documented.Book.Of(job)).Requests);
        Assert.Equal((1, RequestKind.Steer, "Make sure a refund never applies twice", 1, Option<int>.Some(2)), (request.Number, request.Kind, request.Text, request.Attempt, request.Version));
        Assert.Contains("What a person asked that changed the work, to explain in this version:\n\n- Make sure a refund never applies twice\n", brief);
        Assert.Single(documented.Published<ChangeRequested>());
        Assert.Contains("> Make sure a refund never applies twice\n", documented.Files[FakeJobs.Worktree(job), $"{Folder}/changes.md"]);
        Assert.Contains("Produced [v2](v2-deviation.md).", documented.Files[FakeJobs.Worktree(job), $"{Folder}/changes.md"]);
    }

    [Fact]
    public async Task ASendBackOrAMessageThatStartsAnAttemptIsRecordedOnceAndARetryIsNotAsync()
    {
        var documented = Documented.With();
        var (job, _) = await documented.RunningAsync();
        documented.Jobs.Attempt(job, AttemptOrigin.Initial, string.Empty);
        documented.Jobs.Attempt(job, AttemptOrigin.Retry, string.Empty);
        documented.Jobs.Attempt(job, AttemptOrigin.SendBack, "No new service: keep the keys in the ledger");
        documented.Jobs.Attempt(job, AttemptOrigin.Hint, "Also cover partial refunds");

        await documented.ProgressAsync(job, JobStatus.Running);
        await documented.ProgressAsync(job, JobStatus.Running);

        Assert.Equal(
            [(RequestKind.SendBack, 3, "No new service: keep the keys in the ledger"), (RequestKind.Message, 4, "Also cover partial refunds")],
            Outcomes.Present(documented.Book.Of(job)).Requests.Select(request => (request.Kind, request.Attempt, request.Text)));
    }

    [Fact]
    public async Task WithDocsOffNothingIsRecordedAsync()
    {
        var documented = Documented.With(enabled: false);
        var (job, session) = await documented.RunningAsync();

        await SteerAsync(documented, session, "Make sure a refund never applies twice");

        Assert.True(documented.Book.Of(job).IsNone);
        Assert.Empty(documented.Published<ChangeRequested>());
    }

    private static async Task SteerAsync(Documented documented, SessionId session, string text) =>
        await documented.Desk.HandleAsync(new AgentActivity(new MessageQueued(session, TurnId.New(), text)), Cancellation);

    private static async Task KeepAsync(Documented documented, string draft)
    {
        var writer = documented.LastWriter;
        documented.Files.Draft(FakeJobs.Worktree(writer), DocumentationDraft.Path, draft);
        await documented.ProgressAsync(writer, JobStatus.AwaitingReview);
    }
}
