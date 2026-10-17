using Avala.Documentation.Contracts;
using Avala.Testing;

namespace Avala.Documentation.Tests.Writing;

public sealed class SiteDeskTests
{
    [Fact]
    public async Task CreatingTheSiteWritesOnlyTheFilesItLacksInTheDocsFolderWithAvalasC4PlayerAndStartsAJobThatFillsItAsync()
    {
        var documented = Documented.With();
        documented.Files.Draft(Documented.Repository, "docs/index.md", "# Ledger API\n\nOurs.\n");
        documented.Files.Draft(Documented.Repository, "docs/features/refunds/README.md", "# Refund webhooks\n");

        var created = Outcomes.Succeeds(await documented.Entry().CreateSiteAsync(Documented.Repository, Documented.Cancellation));

        Assert.Equal("docs", created.Root);
        Assert.DoesNotContain("docs/index.md", created.Written);
        Assert.Equal("# Ledger API\n\nOurs.\n", documented.Files[Documented.Repository, "docs/index.md"]);
        Assert.All(created.Written, path => Assert.StartsWith("docs/", path));
        Assert.Contains("docs/docfx.json", created.Written);
        Assert.StartsWith("import \"./avala-c4.js\";", documented.Files[Documented.Repository, "docs/template/public/main.js"]);
        Assert.Contains("customElements.define(\"avala-c4\"", documented.Files[Documented.Repository, "docs/template/public/avala-c4.js"]);
        Assert.Equal("- name: Refund webhooks\n  href: refunds/toc.yml\n", documented.Files[Documented.Repository, "docs/features/toc.yml"]);
        var (filler, request) = Assert.Single(documented.Jobs.Submitted);
        Assert.Equal(created.Filler, Sdk.Option<Jobs.Contracts.JobId>.Some(filler));
        Assert.Contains("docs/get-started/quickstart.md", request.Instruction);
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
