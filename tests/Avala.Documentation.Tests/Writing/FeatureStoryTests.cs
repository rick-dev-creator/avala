using Avala.Documentation.Catalog;
using Avala.Documentation.Contracts;
using Avala.Documentation.Writing;
using Avala.Testing;

namespace Avala.Documentation.Tests.Writing;

public sealed class FeatureStoryTests
{
    private const string Repository = "/repos/ledger";

    private static readonly Feature Refunds = new("refund-webhooks", "Refund webhooks");

    [Fact]
    public async Task AFeaturesStoryPlaysEachVersionAgainstTheOneBeforeAsync()
    {
        var documented = Documented.With();
        documented.Files.Draft(Repository, "docs/features/refund-webhooks/v1-proposed.md", "# Refund webhooks\n\n```mermaid\nC4Container\n  Container(webhooks, \"Webhooks\", \"Go\")\n```\n");
        documented.Files.Draft(Repository, "docs/features/refund-webhooks/v2-deviation.md", "# Refund webhooks\n\n```mermaid\nC4Container\n  Container(webhooks, \"Webhooks\", \"Go\")\n  ContainerDb(keys, \"Idempotency\", \"Redis\")\n```\n");
        var entry = documented.Entry();

        var story = Outcomes.Present(await entry.StoryAsync(Repository, Refunds, Documented.Cancellation));

        Assert.Equal([(1, VersionKind.Proposed), (2, VersionKind.Deviation)], story.Versions.Select(version => (version.Number, version.Kind)));
        Assert.Equal([StepKind.Base], story.Versions[0].Steps.Select(step => step.Kind));
        Assert.Equal([(StepKind.Added, "keys")], story.Versions[1].Steps.Select(step => (step.Kind, step.Anchor)));
    }

    [Fact]
    public async Task AJobsDocsTellItsFeatureItsStoryFromItsWorktreeAndTheRepositorysFeaturesAsync()
    {
        var documented = Documented.With();
        var (job, session) = await documented.RunningAsync();
        await documented.PlanAsync(session, "Add a Webhooks container");
        documented.Files.Draft(Documented.FakeJobs.Worktree(documented.LastWriter), DocumentationDraft.Path, "# Handle Stripe refund webhooks\n\n```mermaid\nC4Container\n  Container(webhooks, \"Webhooks\", \"Go\")\n```\n");
        await documented.ProgressAsync(documented.LastWriter, Jobs.Contracts.JobStatus.AwaitingReview);
        documented.Files.Draft(Documented.Repository, "docs/features/payouts/README.md", "# Payout scheduler\n");
        var entry = documented.Entry();

        var docs = Outcomes.Present(await entry.JobDocsAsync(job, Documented.Cancellation));

        Assert.Equal(("Handle Stripe refund webhooks", Documented.Repository), (docs.Documentation.Feature.Title, docs.Repository));
        Assert.Equal([1], Outcomes.Present(docs.Story).Versions.Select(version => version.Number));
        Assert.Equal(["Payout scheduler"], docs.Docs.Features.Select(listed => listed.Feature.Title));
    }

    [Fact]
    public async Task AVersionThatCannotBePlayedIsLeftOutOfItsJobsStoryAndNamedWithTheVersionBeforeItAsync()
    {
        var documented = Documented.With();
        var (job, session) = await documented.RunningAsync();
        await documented.PlanAsync(session, "Add a Webhooks container");
        documented.Files.Draft(Documented.FakeJobs.Worktree(documented.LastWriter), DocumentationDraft.Path, "# Handle Stripe refund webhooks\n\n```mermaid\nC4Container\n  Container(webhooks, \"Webhooks\", \"Go\")\n```\n");
        await documented.ProgressAsync(documented.LastWriter, Jobs.Contracts.JobStatus.AwaitingReview);
        var worktree = Documented.FakeJobs.Worktree(job);
        documented.Files.Draft(worktree, "docs/features/handle-stripe-refund-webhooks/v2-deviation.md", "# Handle Stripe refund webhooks\n");
        documented.Files.Break(worktree, "docs/features/handle-stripe-refund-webhooks/v2-deviation.md", "The docs folder went away");
        var entry = documented.Entry();

        var docs = Outcomes.Present(await entry.JobDocsAsync(job, Documented.Cancellation));

        Assert.Equal([1], Outcomes.Present(docs.Story).Versions.Select(version => version.Number));
        Assert.Equal([new UnplayedVersion("v2-deviation.md") { After = "v1-proposed.md" }], docs.Unplayed);
    }

    [Fact]
    public async Task AVersionWhoseDiagramNamesAnElementTwiceIsLeftOutOfItsJobsStoryWithTheNameAndTheNextIsPlayedAsync()
    {
        var documented = Documented.With();
        var (job, session) = await documented.RunningAsync();
        await documented.PlanAsync(session, "Add a Webhooks container");
        documented.Files.Draft(Documented.FakeJobs.Worktree(documented.LastWriter), DocumentationDraft.Path, "# Handle Stripe refund webhooks\n\n```mermaid\nC4Container\n  Container(webhooks, \"Webhooks\", \"Go\")\n```\n");
        await documented.ProgressAsync(documented.LastWriter, Jobs.Contracts.JobStatus.AwaitingReview);
        var worktree = Documented.FakeJobs.Worktree(job);
        documented.Files.Draft(worktree, "docs/features/handle-stripe-refund-webhooks/v2-deviation.md", "# Handle Stripe refund webhooks\n\n```mermaid\nC4Container\n  Container(log, \"Changelog\", \"Markdown\")\n  Container(log, \"Changelog again\", \"Markdown\")\n```\n");
        documented.Files.Draft(worktree, "docs/features/handle-stripe-refund-webhooks/v3-as-built.md", "# Handle Stripe refund webhooks\n\n```mermaid\nC4Container\n  Container(webhooks, \"Webhooks\", \"Go\")\n  ContainerDb(keys, \"Idempotency\", \"Redis\")\n```\n");
        var entry = documented.Entry();

        var docs = Outcomes.Present(await entry.JobDocsAsync(job, Documented.Cancellation));

        Assert.Equal([1, 3], Outcomes.Present(docs.Story).Versions.Select(version => version.Number));
        Assert.Equal([new UnplayedVersion("v2-deviation.md") { Repeated = "log" }], docs.Unplayed);
    }

    [Fact]
    public async Task AFeatureWithoutVersionsHasNoStoryAsync()
    {
        var documented = Documented.With();
        var entry = documented.Entry();

        Assert.True((await entry.StoryAsync(Repository, Refunds, Documented.Cancellation)).IsNone);
    }
}
