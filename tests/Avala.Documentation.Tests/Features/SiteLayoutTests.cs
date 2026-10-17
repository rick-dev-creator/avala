using Avala.Documentation.Features;

namespace Avala.Documentation.Tests.Features;

public sealed class SiteLayoutTests
{
    [Theory]
    [InlineData("docs/features", "docs", "features")]
    [InlineData("documentation/features", "documentation", "features")]
    [InlineData("site/docs/features", "site/docs", "features")]
    [InlineData("features", "docs", "features")]
    public void TheSiteLivesInTheParentOfTheFeaturesFolder(string folder, string root, string featured)
    {
        Assert.Equal(root, SiteLayout.RootOf(folder));
        Assert.Equal(featured, SiteLayout.FeaturesFolder(root, folder));
    }

    [Fact]
    public void TheSiteIsLaidOutTheWayMicrosoftLearnIsWithEachPagesArticleTypeAndAvalasTemplate()
    {
        var files = SiteLayout.Files("docs", "docs/features", "ledger-api", [("refund-webhooks", "Refund webhooks")], [new SiteFile("public/main.js", "export default {};\n")]).ToDictionary(file => file.Path, file => file.Content);

        Assert.Equal(
            ["docs/docfx.json", "docs/toc.yml", "docs/index.md", "docs/get-started/quickstart.md", "docs/concepts/architecture.md", "docs/concepts/glossary.md", "docs/how-to/index.md", "docs/tutorials/index.md", "docs/reference/configuration.md", "docs/features/toc.yml", "docs/architecture/decisions/index.md", "docs/architecture/decisions/toc.yml", "docs/whats-new.md", "docs/media/.gitkeep", "docs/includes/.gitkeep", "docs/template/public/main.js"],
            files.Keys);
        Assert.StartsWith("---\ntitle: ledger-api\nms.topic: overview\n---", files["docs/index.md"]);
        Assert.Contains("ms.topic: quickstart", files["docs/get-started/quickstart.md"]);
        Assert.Contains("ms.topic: reference", files["docs/reference/configuration.md"]);
        Assert.Contains("\"template\": [ \"default\", \"modern\", \"template\" ]", files["docs/docfx.json"]);
        Assert.Contains("\"exclude\": [ \"_site/**\", \"template/**\" ]", files["docs/docfx.json"]);
        Assert.Contains("  href: features/toc.yml", files["docs/toc.yml"]);
        Assert.Equal("- name: Refund webhooks\n  href: refund-webhooks/toc.yml\n", files["docs/features/toc.yml"]);
    }
}
