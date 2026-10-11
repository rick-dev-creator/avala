using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Workbench.Inspector;

namespace Avala.Workbench.Tests.Inspector;

public sealed class DelegationSectionViewModelScripts : IDisposable
{
    private readonly Bench bench = new();

    [Fact]
    public async Task AJobInFocusShowsItsParentAndEachChildWithWhatItReported()
    {
        using var section = new DelegationSectionViewModel(bench.Inspected());

        await section.FocusAsync(InspectedJobs.Reviewed(bench).Job, bench);

        Assert.Equal(
            ("Delegated by Harden the auth endpoints", "Write the login tests · approved · integrated into its parent · not yet told to its parent", false),
            (section.Parent, string.Join("|", section.Children), section.IsEmpty));
    }

    [Fact]
    public async Task ARefusedDelegationIsListedWithItsReason()
    {
        var job = bench.Job("Split CheckoutPage into steps", JobStatus.Running);
        bench.Resources.Delegations.Add(new DelegationRecord(SessionId.New(), new ItemId("delegate"), "Rewrite the payment step in Svelte", DateTimeOffset.UnixEpoch)
        {
            Parent = job.Job,
            Refusal = DelegationError.NotDeclared,
        });
        using var section = new DelegationSectionViewModel(bench.Inspected());

        await section.FocusAsync(job.Job, bench);

        Assert.Equal(["Refused: Rewrite the payment step in Svelte · the repository declares no delegation to that connection"], section.Children);
    }

    [Fact]
    public async Task AReadOnlyChildIsListedWithItsRole()
    {
        var job = bench.Job("Ship the release notes", JobStatus.Running);
        var child = bench.Job("Review the release notes", JobStatus.Discarded);
        bench.Catalog.Change(child.Job, history => history with { Summary = history.Summary with { Parent = job.Job } });
        bench.Resources.Delegations.Add(new DelegationRecord(SessionId.New(), new ItemId("delegate"), "Review the release notes", DateTimeOffset.UnixEpoch)
        {
            Parent = job.Job,
            Child = child.Job,
            Role = ChildRole.Reviewer,
            Report = new ChildReport(child.Job, ChildOutcome.Reported, JobStatus.Discarded, DateTimeOffset.UnixEpoch),
        });
        using var section = new DelegationSectionViewModel(bench.Inspected());

        await section.FocusAsync(job.Job, bench);

        Assert.Equal(["Review the release notes · reviewer, read-only · discarded · reported its findings · not yet told to its parent"], section.Children);
    }

    [Fact]
    public async Task AJobThatDelegatedNothingSaysSoOnlyOnceLoaded()
    {
        using var section = new DelegationSectionViewModel(bench.Inspected());
        var before = section.IsEmpty;

        await section.FocusAsync(bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running).Job, bench);

        Assert.Equal((false, true), (before, section.IsEmpty));
    }

    [Fact]
    public async Task TheHeaderFactCountsTheSubAgents()
    {
        using var section = new DelegationSectionViewModel(bench.Inspected());

        await section.FocusAsync(InspectedJobs.Reviewed(bench).Job, bench);

        Assert.Equal("1 sub-agent", section.Fact);
    }

    public void Dispose() => bench.Dispose();
}
