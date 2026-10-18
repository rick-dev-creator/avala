using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Documentation.Tests.Writing;

public sealed class SiteDeskTests
{
    [Fact]
    public async Task CreatingTheSiteWritesNothingInTheWorkingCopyAndSubmitsTheJobThatLaysItOutAndFillsItAsync()
    {
        var documented = Documented.With();

        var created = Outcomes.Succeeds(await documented.Entry().CreateSiteAsync(Documented.Repository, Documented.Cancellation));

        var (job, request) = Assert.Single(documented.Jobs.Submitted);
        Assert.Equal(new SiteCreated("docs", job), created);
        Assert.Equal((Documented.Repository, true, true), (request.RepositoryPath, request.Instruction.Contains("docs/get-started/quickstart.md", StringComparison.Ordinal), request.Instruction.Contains("docs/whats-new.md, under its lead and newest first", StringComparison.Ordinal)));
        Assert.Null(documented.Files[Documented.Repository, "docs/docfx.json"]);
        Assert.Equal(Option<JobId>.Some(job), (await documented.Entry().SiteAsync(Documented.Repository, Documented.Cancellation)).Preparing);
    }

    [Fact]
    public async Task TheSiteJobFindsTheSiteLaidOutInItsWorktreeBeforeItsFirstTurnKeepingWhatTheRepositoryHasAsync()
    {
        var documented = Documented.With();
        documented.Files.Draft(Documented.Repository, "docs/features/refunds/README.md", "# Refund webhooks\n");
        var job = Outcomes.Succeeds(await documented.Entry().CreateSiteAsync(Documented.Repository, Documented.Cancellation)).Job;
        documented.Jobs.Move(job, JobStatus.Running);
        var worktree = Documented.FakeJobs.Worktree(job);
        documented.Files.Draft(worktree, "docs/index.md", "# Ledger API\n\nOurs.\n");

        var briefed = await documented.Skeleton().BriefAsync(job, Documented.Cancellation);
        var again = await documented.Skeleton().BriefAsync(job, Documented.Cancellation);

        Assert.StartsWith("Avala laid out the site in docs/:", Outcomes.Present(briefed).Text, StringComparison.Ordinal);
        Assert.True(again.IsNone);
        Assert.Equal("# Ledger API\n\nOurs.\n", documented.Files[worktree, "docs/index.md"]);
        Assert.Contains("\"template\": [ \"default\", \"modern\", \"template\" ]", documented.Files[worktree, "docs/docfx.json"], StringComparison.Ordinal);
        Assert.Contains("customElements.define(\"avala-c4\"", documented.Files[worktree, "docs/template/public/avala-c4.js"], StringComparison.Ordinal);
        Assert.Null(documented.Files[Documented.Repository, "docs/docfx.json"]);
    }

    [Fact]
    public async Task AJobThatIsNotTheSitesFindsNothingLaidOutAsync()
    {
        var documented = Documented.With();
        var job = documented.Jobs.Add(JobStatus.Running, "Add the refunds endpoint", Option<JobId>.None);

        Assert.True((await documented.Skeleton().BriefAsync(job, Documented.Cancellation)).IsNone);
        Assert.Null(documented.Files[Documented.FakeJobs.Worktree(job), "docs/docfx.json"]);
    }

    [Theory]
    [InlineData("Discarded")]
    [InlineData("Failed")]
    [InlineData("Approved")]
    public async Task ASiteJobThatEndedNoLongerPreparesTheSiteAsync(string ended)
    {
        var documented = Documented.With();
        var job = Outcomes.Succeeds(await documented.Entry().CreateSiteAsync(Documented.Repository, Documented.Cancellation)).Job;

        documented.Jobs.Move(job, Enum.Parse<JobStatus>(ended));

        Assert.True((await documented.Entry().SiteAsync(Documented.Repository, Documented.Cancellation)).Preparing.IsNone);
    }

    [Fact]
    public async Task TheSiteSaysWhetherItExistsAndWhetherDocfxCanServeItAsync()
    {
        var documented = Documented.With();
        var before = await documented.Entry().SiteAsync(Documented.Repository, Documented.Cancellation);
        documented.Files.Draft(Documented.Repository, "docs/docfx.json", "{}");
        documented.Docfx.IsInstalled = false;

        var after = await documented.Entry().SiteAsync(Documented.Repository, Documented.Cancellation);

        Assert.Equal(new DocsSite(false, "docs", true), before);
        Assert.Equal(new DocsSite(true, "docs", false), after);
    }
}
