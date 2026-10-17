using Avala.Documentation.Contracts;
using Avala.Documentation.Features;
using Avala.Sdk;

namespace Avala.Documentation.Tests.Features;

public sealed class WriterBriefTests
{
    private static readonly Feature Refunds = new("refund-webhooks", "Refund webhooks");

    [Fact]
    public void TheBriefAsksForOneDraftFileWithTheJobsInstructionAndPlanAndAMermaidC4()
    {
        var brief = new WriterBrief(Refunds, 1, VersionKind.Proposed, "Handle Stripe refund webhooks", ["Add a Webhooks container", "Write the refund to the ledger"], Option<string>.None).Text();

        Assert.StartsWith("Write version 1 of the feature \"Refund webhooks\" as one Markdown file, .avala/docs/version.md.", brief);
        Assert.Contains("records the plan the job's agent proposed", brief);
        Assert.Contains("Handle Stripe refund webhooks", brief);
        Assert.Contains("1. Add a Webhooks container\n2. Write the refund to the ledger\n", brief);
        Assert.Contains("Mermaid C4", brief);
        Assert.Contains("{#alias}", brief);
        Assert.DoesNotContain("previous version", brief);
    }

    [Fact]
    public void ADeviationsBriefCarriesThePreviousVersionToBuildOn()
    {
        var brief = new WriterBrief(Refunds, 2, VersionKind.Deviation, "Handle refunds", ["Keep idempotency keys"], "# Refund webhooks\n\nv1 text").Text();

        Assert.Contains("The job's plan changed.", brief);
        Assert.Contains("The previous version, to build on:\n\n# Refund webhooks\n\nv1 text\n", brief);
    }
}
