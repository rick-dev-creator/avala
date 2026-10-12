using Avala.Forges.Contracts;
using Avala.Workbench.Inspector;
using Avala.Workbench.RepositoryChoice;
using Avala.Workbench.Tests.RepositoryChoice;

namespace Avala.Workbench.Tests.Inspector;

public sealed class PublishRepositoryViewModelScripts
{
    private const string Shop = "/home/ada/code/shop";

    private readonly FakePublishing publishing = new();

    [Fact]
    public async Task ARepositoryWithoutARemoteIsOfferedPublishingAsync()
    {
        var publisher = await ShownAsync();

        Assert.Equal((true, true, PublishPhrases.NoRemote), (publisher.IsShown, publisher.CanOffer, publisher.Remote.Show(hidden: false)));
    }

    [Fact]
    public async Task ARepositoryWhoseOriginIsOnAForgeSaysWhereAndOffersNothingAsync()
    {
        publishing.Remotes[Shop] = new RepositoryRemote("origin", "git@github.com:octo/shop.git") { Address = new RepositoryAddress("github.com", "octo", "shop"), Forge = FakePublishing.GitHub };

        var publisher = await ShownAsync();

        Assert.Equal((false, "origin is octo/shop on github (GitHub)."), (publisher.CanOffer, publisher.Remote.Show(hidden: false)));
        Assert.False(publisher.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task PublishingProposesTheFirstForgeTheFolderNameAndPrivateAsync()
    {
        var publisher = await ShownAsync();

        await publisher.StartCommand.ExecuteAsync(null);

        Assert.Equal((true, "github", "GitHub", "shop", true), (publisher.IsOpen, publisher.Options.Target, publisher.Options.ForgeKind, publisher.Options.Name, publisher.Options.IsPrivate));
        Assert.True(publisher.PublishCommand.CanExecute(null));
    }

    [Fact]
    public async Task APublishedRepositoryIsPushedAsChosenAndItsOriginIsThenKnownAsync()
    {
        var publisher = await ShownAsync();
        await publisher.StartCommand.ExecuteAsync(null);
        publisher.Options.Name = "storefront";
        publisher.Options.IsPublic = true;

        await publisher.PublishCommand.ExecuteAsync(null);

        Assert.Equal([(Shop, FakePublishing.GitHub.Forge, new RepositoryDraft("storefront", Private: false))], publishing.Published);
        Assert.Equal((false, false, "origin is ada/storefront on github (GitHub)."), (publisher.IsOpen, publisher.CanOffer, publisher.Remote.Show(hidden: false)));
        Assert.Equal("Published ada/storefront on github and pushed main; origin is git@github.com:ada/storefront.git.", publisher.Notice.Show(hidden: false));
    }

    [Fact]
    public async Task ARefusedPublishStaysOpenWithTheReasonAsync()
    {
        publishing.Refusal = ForgeError.PushUnauthorized;
        var publisher = await ShownAsync();
        await publisher.StartCommand.ExecuteAsync(null);

        await publisher.PublishCommand.ExecuteAsync(null);

        Assert.Equal((true, PublishPhrases.Failure(ForgeError.PushUnauthorized)), (publisher.IsOpen, publisher.Error));
    }

    private async Task<PublishRepositoryViewModel> ShownAsync()
    {
        var publisher = new PublishRepositoryViewModel(publishing, new PublishOptionsViewModel(publishing));
        publisher.Show(Shop);
        await publisher.Reading;

        return publisher;
    }
}
