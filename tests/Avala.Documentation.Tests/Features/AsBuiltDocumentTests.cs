using Avala.Documentation.Contracts;
using Avala.Documentation.Features;
using Avala.Sdk;

namespace Avala.Documentation.Tests.Features;

public sealed class AsBuiltDocumentTests
{
    private static readonly Feature Refunds = new("refund-webhooks", "Refund webhooks");

    [Fact]
    public void AsBuiltKeepsTheLastVersionsBodyUnderItsTitleAndAddsTheEvidence()
    {
        var evidence = new BuildEvidence(
            2,
            [new CheckOutcome("tests", "Passed"), new CheckOutcome("lint", "Skipped")],
            [new ChangedFile("internal/webhooks/handler.go", 84, 2), new ChangedFile("assets/logo.png", Option<int>.None, Option<int>.None)]);

        var document = AsBuiltDocument.Compose(Refunds, 3, "# Refund webhooks\n\nThe C4 and its sections.\n", evidence);

        Assert.Equal(
            "# Refund webhooks\n\nAs built: what attempt 2 of the job delivered for review, from version 3. Written by Avala from the job's evidence.\n\nThe C4 and its sections.\n\n## Evidence {#evidence}\n\n- Checks: tests passed, lint skipped\n- Files changed: 2, +84 −2\n  - `internal/webhooks/handler.go` +84 −2\n  - `assets/logo.png`\n",
            document);
    }

    [Fact]
    public void WithoutChecksAsBuiltSaysNoneWereDeclared() =>
        Assert.Contains("- Checks: none declared\n", AsBuiltDocument.Compose(Refunds, 1, Option<string>.None, new BuildEvidence(1, [], [])));
}
