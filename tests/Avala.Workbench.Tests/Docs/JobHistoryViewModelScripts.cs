using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Docs;

namespace Avala.Workbench.Tests.Docs;

public sealed class JobHistoryViewModelScripts
{
    private static readonly JobId Job = JobId.New();

    [Fact]
    public void TheHistoryChainsTheJobsVersionsWithTheRequestsThatProducedThemAndSummarizesTheLast()
    {
        var history = new JobHistoryViewModel();

        history.Show(Docs());

        Assert.Equal([("Plan v1 · proposed", true), ("Change request #1", false), ("Plan v2 · deviation", true), ("Change request #2", false)], history.Steps.Select(step => (step.Title, step.IsVersion)));
        Assert.Equal(("Plan v2 · deviation", "The plan did not name webhooks, but v79 changes how they are signed."), (history.Heading, history.Happened));
        Assert.Equal("You asked “Once only”. The plan changed to do it.", history.Why);
        Assert.Equal(["+ Webhooks", "~ Payments"], history.Changed);
        Assert.Equal("v2-deviation.md · plans/v2.md", history.Records);
    }

    [Fact]
    public void ChoosingAVersionSummarizesItAndSaysWhichOneWasChosen()
    {
        var history = new JobHistoryViewModel();
        var chosen = new List<int>();
        history.VersionChosen += (_, number) => chosen.Add(number);
        history.Show(Docs());

        history.Steps[0].ChooseCommand.Execute(null);

        Assert.Equal(("Plan v1 · proposed", "None. It is the plan the agent proposed."), (history.Heading, history.Why));
        Assert.Equal([1], chosen);
        Assert.Equal([true, false, false, false], history.Steps.Select(step => step.IsChosen));
    }

    private static JobDocs Docs()
    {
        var documented = new JobDocumentation(Job, new Feature("payments", "Payments on stripe-go v79"))
        {
            Versions = [new FeatureVersion(1, VersionKind.Proposed, "docs/features/payments/v1-proposed.md", DateTimeOffset.UnixEpoch), new FeatureVersion(2, VersionKind.Deviation, "docs/features/payments/v2-deviation.md", DateTimeOffset.UnixEpoch)],
            Requests = [new ChangeRequest(1, RequestKind.Steer, "Once only", DateTimeOffset.UnixEpoch, 1) { Version = 2 }, new ChangeRequest(2, RequestKind.SendBack, "Keep the keys in the ledger", DateTimeOffset.UnixEpoch, 1)],
        };

        return new JobDocs(documented, "/repos/ledger", DesignStories.Ledger(), RepositoryDocs.Off);
    }
}
