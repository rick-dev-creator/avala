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
        var entry = new DocumentationEntry(documented.Rules, new FeatureCatalog(documented.Files, documented.Files), documented.Book, documented.Files);

        var story = Outcomes.Present(await entry.StoryAsync(Repository, Refunds, Documented.Cancellation));

        Assert.Equal([(1, VersionKind.Proposed), (2, VersionKind.Deviation)], story.Versions.Select(version => (version.Number, version.Kind)));
        Assert.Equal([StepKind.Base], story.Versions[0].Steps.Select(step => step.Kind));
        Assert.Equal([(StepKind.Added, "keys")], story.Versions[1].Steps.Select(step => (step.Kind, step.Anchor)));
    }

    [Fact]
    public async Task AFeatureWithoutVersionsHasNoStoryAsync()
    {
        var documented = Documented.With();
        var entry = new DocumentationEntry(documented.Rules, new FeatureCatalog(documented.Files, documented.Files), documented.Book, documented.Files);

        Assert.True((await entry.StoryAsync(Repository, Refunds, Documented.Cancellation)).IsNone);
    }
}
