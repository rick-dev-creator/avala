using Avala.Documentation.Contracts;
using Avala.Documentation.Features;
using Avala.Sdk;

namespace Avala.Documentation.Tests.Features;

public sealed class FeatureFolderTests
{
    private static readonly FeatureFolder Refunds = new("docs/features", new Feature("refund-webhooks", "Refund webhooks"));

    [Fact]
    public void AFeaturesVersionsAreItsNumberedFilesInOrderAndNothingElseInItsFolder()
    {
        var versions = Refunds.VersionsIn(
        [
            "docs/features/refund-webhooks/v10-as-built.md",
            "docs/features/refund-webhooks/v2-deviation.md",
            "docs/features/refund-webhooks/README.md",
            "docs/features/refund-webhooks/plans/v1.md",
            "docs/features/refund-webhooks/v1-proposed.md",
            "docs/features/refund-webhooks/v3-notes.md",
            "docs/features/payouts/v4-proposed.md",
            "src/v5-proposed.md",
        ]);

        Assert.Equal(
            [new VersionFile(1, VersionKind.Proposed, "v1-proposed.md"), new VersionFile(2, VersionKind.Deviation, "v2-deviation.md"), new VersionFile(10, VersionKind.AsBuilt, "v10-as-built.md")],
            versions);
    }

    [Fact]
    public void AVersionAndAPlanAreWrittenAtTheirNumberedPaths()
    {
        Assert.Equal("docs/features/refund-webhooks/v3-as-built.md", Refunds.Version(3, VersionKind.AsBuilt));
        Assert.Equal("docs/features/refund-webhooks/plans/v2.md", Refunds.Plan(2));
    }

    [Fact]
    public void TheIndexAndTheTableOfContentsListTheVersionsInOrder()
    {
        VersionFile[] versions = [new(1, VersionKind.Proposed, "v1-proposed.md"), new(2, VersionKind.AsBuilt, "v2-as-built.md")];

        Assert.Equal(
            "# Refund webhooks\n\nWritten by Avala as the feature is built. Each version is a moment of the feature, with its C4 diagram.\n\n| Version | Kind | Document |\n| --- | --- | --- |\n| v1 | Proposed | [v1-proposed.md](v1-proposed.md) |\n| v2 | As built | [v2-as-built.md](v2-as-built.md) |\n",
            Refunds.Index(versions));
        Assert.Equal(
            "- name: Overview\n  href: README.md\n- name: v1 · Proposed\n  href: v1-proposed.md\n- name: v2 · As built\n  href: v2-as-built.md\n",
            FeatureFolder.TableOfContents(versions));
    }

    [Fact]
    public void APlanIsKeptAsItsNumberedSteps() =>
        Assert.Equal("# Plan v1\n\n1. Add the webhooks\n2. Run the tests\n", FeatureFolder.PlanDocument(1, ["Add the webhooks", "Run the tests"]));

    [Theory]
    [InlineData("# Refund webhooks\n\nVersions.", "Refund webhooks")]
    [InlineData("Intro\n#  Payout scheduler \n", "Payout scheduler")]
    [InlineData("## Not a title", null)]
    public void AFeaturesTitleIsTheFirstHeadingOfItsIndex(string readme, string? title) =>
        Assert.Equal(title is null ? Option<string>.None : title, FeatureFolder.TitleIn(readme));
}
