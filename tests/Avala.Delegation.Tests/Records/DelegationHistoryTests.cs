using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Delegation.Contracts;
using Avala.Delegation.Storage;
using Avala.Delegation.Tests.Delegating;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Storage;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Delegation.Tests.Records;

public sealed class DelegationHistoryTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheRecordsOfEarlierRunsCountForTheirParentAndChildAlongsideEveryRecordThisRunStoresAsync()
    {
        await using var desk = new Desk();
        var (parent, child) = (JobId.New(), JobId.New());
        var earlier = Reported(parent, child);
        var refused = Refused(parent);
        desk.Store.Earlier = [earlier, refused];

        await desk.Book.RunAsync(Cancellation);
        await desk.StartedAsync();
        var delegated = await desk.DelegateAsync("todo");

        Assert.Equal([earlier, refused], desk.Book.OfParent(parent));
        Assert.Equal(Option<DelegationRecord>.Some(earlier), desk.Book.OfChild(child));
        Assert.Equal(Option<JobId>.Some(delegated), Assert.Single(desk.Store.Recorded).Child);
    }

    [Fact]
    public async Task ARecordSurvivesAReopeningAsItsLatestVersionAndEarlierRunsLeaveOutWhatThisRunStoredAsync()
    {
        await using var folder = new TemporaryFolder();
        var (parent, child) = (JobId.New(), JobId.New());
        var reported = Reported(parent, child);
        await using (var first = new SqliteDelegationStore(new AvalaPaths(folder.Path)))
        {
            await first.RecordAsync(reported with { Report = Option<ChildReport>.None }, Cancellation);
            await first.RecordAsync(reported, Cancellation);
            await first.RecordAsync(Refused(parent), Cancellation);
        }

        await using var second = new SqliteDelegationStore(new AvalaPaths(folder.Path));
        await second.RecordAsync(Refused(JobId.New()), Cancellation);

        Assert.Equal(StoredJson.Write<IReadOnlyList<DelegationRecord>>([reported, Refused(parent)]), StoredJson.Write(await second.EarlierRunsAsync(Cancellation)));
    }

    private static DelegationRecord Reported(JobId parent, JobId child)
    {
        var verified = new VerificationReport(child, 1, VerificationOutcome.Passed, Option<FileOrigin>.None, [], GateVerdict.Pass, Nine);

        return new DelegationRecord(new SessionId(Guid.Parse("0199c3a1-7a10-7000-8000-000000000001")), new ItemId("notes"), "Write the release notes", Nine)
        {
            Parent = parent,
            Depth = 1,
            Child = child,
            Connection = new ConnectionName("work"),
            Choice = new ConnectionChoice(new ConnectionName("work"), ChoiceReason.MostCapacity, [], Nine),
            Autonomy = Autonomy.Autonomous,
            Report = new ChildReport(child, ChildOutcome.Integrated, JobStatus.Approved, Nine.AddMinutes(3))
            {
                Summary = "Wrote NOTES.md.",
                Files = [new FileChange("NOTES.md", ChangeKind.Added, 3, 0)],
                Verification = verified,
                Spent = [new Cost(0.05m, "USD")],
                Tokens = 1_200,
                Carve = new BudgetCarve(parent, child, [new Cost(0.50m, "USD")], 500L, 0.5, Nine),
                Delivery = new ApprovalDelivery("merge", "avala/notes", "c0ffee"),
            },
        };
    }

    private static DelegationRecord Refused(JobId parent) =>
        new(new SessionId(Guid.Parse("0199c3a1-7a10-7000-8000-000000000001")), new ItemId("recurse"), "Delegate again", Nine.AddMinutes(1))
        {
            Parent = parent,
            Depth = 3,
            Refusal = DelegationError.DepthExceeded,
        };
}
