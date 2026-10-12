using Avala.Forges.Contracts;
using Avala.Workbench.Presenting;

namespace Avala.Workbench.RepositoryChoice;

internal static class PublishPhrases
{
    public const string NoTargets = "Connect a forge account in Settings to publish.";

    public const string NoRemote = "This repository has no remote yet, so a pull request has nowhere to go.";

    public static string Published(PublishedRepository published) =>
        $"Published {Named(published.Repository)} on {published.Forge.Forge.Value} and pushed {published.Branch}; origin is {published.PushUrl}.";

    public static string Remote(RepositoryRemote remote) =>
        remote.Forge.Match(
            forge => $"{remote.Remote} is {remote.Address.Match(Named, () => remote.Url)} on {forge.Forge.Value} ({forge.ForgeKind}).",
            () => $"{remote.Remote} is {remote.Url}, on no forge connection of forges.json.");

    public static string Failure(ForgeError error) => error switch
    {
        ForgeError.NameTaken => "A repository with that name already exists on the forge: choose another name.",
        ForgeError.RemoteExists => "This repository already has an origin elsewhere: Avala leaves it alone.",
        ForgeError.RepositoriesUnsupported => "That forge connection cannot create repositories.",
        ForgeError.Unauthorized => "The forge refused the connection's token: it needs the right to create repositories.",
        ForgeError.PushUnauthorized => "The repository was created and added as origin, but git was refused when pushing. Set up the credentials git uses for that host, an SSH key or a credential helper such as gh auth setup-git, then publish again: Avala only pushes this time.",
        ForgeError.PushRejected => "The repository was created and added as origin, but the forge refused the push.",
        ForgeError.NoBaseBranch => "There is nothing to push yet: the repository needs a commit on a branch.",
        _ => $"Publishing failed: {ForgePhrases.Error(error)}",
    };

    private static string Named(RepositoryAddress address) => $"{address.Owner}/{address.Name}";
}
