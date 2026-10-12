using Avala.Forges.Contracts;
using Avala.Workbench.RepositoryChoice;

namespace Avala.Workbench.Tests.RepositoryChoice;

public sealed class PublishOptionsViewModelScripts
{
    private readonly FakePublishing publishing = new();

    [Fact]
    public async Task ANameTypedForTheForgeIsKeptWhenTheSuggestionChangesAsync()
    {
        var options = new PublishOptionsViewModel(publishing);
        await options.LoadAsync("invoices", CancellationToken.None);
        var suggested = options.Name;

        options.Name = "billing";
        options.Suggest("receipts");

        Assert.Equal(("invoices", "billing"), (suggested, options.Name));
    }

    [Fact]
    public async Task ChoosingAnotherConnectionShowsItsForgeAsync()
    {
        publishing.Targets.Add(new PublishTarget(new ForgeName("codeberg"), "Forgejo"));
        var options = new PublishOptionsViewModel(publishing);
        await options.LoadAsync("invoices", CancellationToken.None);
        var first = (options.Target, options.ForgeKind);

        options.Target = "codeberg";

        Assert.Equal((("github", "GitHub"), "Forgejo"), (first, options.ForgeKind));
    }
}
