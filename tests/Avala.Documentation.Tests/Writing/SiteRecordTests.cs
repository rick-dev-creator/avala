using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using static Avala.Documentation.Tests.Writing.Documented;

namespace Avala.Documentation.Tests.Writing;

public sealed class SiteRecordTests
{
    private const string Folder = "docs/features/handle-stripe-refund-webhooks";

    [Fact]
    public async Task ADeviationsDecisionRecordIsNumberedIntoTheDecisionsAndLinkedBothWaysWithItsVersionAsync()
    {
        var documented = Documented.With();
        var (job, session) = await documented.RunningAsync();
        var worktree = FakeJobs.Worktree(job);
        documented.Files.Draft(worktree, "docs/architecture/decisions/0001-use-postgres.md", "# Use Postgres\n");
        documented.Files.Draft(worktree, "docs/architecture/decisions/toc.yml", "- name: Architecture decisions\n  href: index.md\n");
        await documented.PlanAsync(session, "Add a Webhooks container");
        documented.Files.Draft(FakeJobs.Worktree(documented.LastWriter), DocumentationDraft.Path, "# v1\n");
        await documented.ProgressAsync(documented.LastWriter, JobStatus.AwaitingReview);
        await documented.PlanAsync(session, "Add a Webhooks container", "Keep keys in the ledger");
        var deviation = documented.LastWriter;
        documented.Files.Draft(FakeJobs.Worktree(deviation), DocumentationDraft.Path, "# v2\n\nThe keys moved.\n");
        documented.Files.Draft(FakeJobs.Worktree(deviation), DocumentationDraft.Decision, "# Keep keys in the ledger\n\n## Status\n\nAccepted\n");

        await documented.ProgressAsync(deviation, JobStatus.AwaitingReview);

        Assert.Contains("- [ADR-0002: Keep keys in the ledger](../../architecture/decisions/0002-keep-keys-in-the-ledger.md)", documented.Files[worktree, $"{Folder}/v2-deviation.md"]);
        Assert.Contains("- [Handle Stripe refund webhooks v2](../../features/handle-stripe-refund-webhooks/v2-deviation.md)", documented.Files[worktree, "docs/architecture/decisions/0002-keep-keys-in-the-ledger.md"]);
        Assert.EndsWith("- name: ADR-0002 Keep keys in the ledger\n  href: 0002-keep-keys-in-the-ledger.md\n", documented.Files[worktree, "docs/architecture/decisions/toc.yml"]);
        Assert.DoesNotContain("## Decisions", documented.Files[worktree, $"{Folder}/v1-proposed.md"]);
    }

    [Fact]
    public async Task AsBuiltAddsItsEntryToWhatsNewWhenTheSiteHasOneAsync()
    {
        var documented = Documented.With();
        var (job, session) = await documented.RunningAsync();
        var worktree = FakeJobs.Worktree(job);
        documented.Files.Draft(worktree, "docs/whats-new.md", "# What's new\n\nNewest first.\n");
        await documented.PlanAsync(session, "Add a Webhooks container");
        documented.Files.Draft(FakeJobs.Worktree(documented.LastWriter), DocumentationDraft.Path, "# Handle Stripe refund webhooks\n\nRefunds apply once.\n");
        await documented.ProgressAsync(documented.LastWriter, JobStatus.AwaitingReview);

        await documented.ProgressAsync(job, JobStatus.AwaitingReview);

        var news = documented.Files[worktree, "docs/whats-new.md"]!;
        Assert.Contains(" · Handle Stripe refund webhooks\n\nRefunds apply once.\n\n[As built](features/handle-stripe-refund-webhooks/v2-as-built.md)", news);
        Assert.StartsWith("# What's new\n\nNewest first.\n\n## ", news);
    }
}
