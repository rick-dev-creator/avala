using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Sidebar;

public sealed class CandidateRowViewModelScripts
{
    private readonly FakeCatalog catalog = new();

    [Fact]
    public void ACandidateIsNamedByItsOrdinalAndTellsWhatItsJobIsDoing()
    {
        var job = new BoardJob(catalog.Add("Fix JPY rounding", JobStatus.Running).Summary, Transcript.Empty);

        ViewModelScript.Given(new CandidateRowViewModel(2, job))
            .When(row => row.Update(job with { Summary = job.Summary with { Status = JobStatus.Checking } }))
            .Then(row => Assert.Equal(("Candidate 2", "verifying", StatusKind.Checking, true), (row.Title, row.Fact, row.Dot.Kind, row.IsChild)));
    }
}
