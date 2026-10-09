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
            ("Delegated by Harden the auth endpoints", "Write the login tests · Approved · Integrated", false),
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

        Assert.Equal(["Refused: Rewrite the payment step in Svelte · NotDeclared"], section.Children);
    }

    [Fact]
    public async Task AJobThatDelegatedNothingSaysSoOnlyOnceLoaded()
    {
        using var section = new DelegationSectionViewModel(bench.Inspected());
        var before = section.IsEmpty;

        await section.FocusAsync(bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running).Job, bench);

        Assert.Equal((false, true), (before, section.IsEmpty));
    }

    public void Dispose() => bench.Dispose();
}
