using System.Collections.Frozen;
using Avala.Components.Privacy;
using Avala.Forges.Contracts;
using Avala.Workbench.Presenting;

namespace Avala.Workbench.RepositoryChoice;

internal static class PublishPhrases
{
    public const string NoTargets = "Connect a forge account in Settings to publish.";

    public const string NoRemote = "This repository has no remote yet, so a pull request has nowhere to go.";

    private static readonly FrozenDictionary<ForgeError, string> Failures = new Dictionary<ForgeError, string>
    {
        [ForgeError.NameTaken] = "A repository with that name already exists on the forge: choose another name.",
        [ForgeError.RemoteExists] = "This repository already has an origin elsewhere: Avala leaves it alone.",
        [ForgeError.RepositoriesUnsupported] = "That forge connection cannot create repositories.",
        [ForgeError.Unauthorized] = "The forge refused the connection's token: it needs the right to create repositories.",
        [ForgeError.PushUnauthorized] = "The repository was created and added as origin, but git was refused when pushing. Set up the credentials git uses for that host, an SSH key or a credential helper such as gh auth setup-git, then publish again: Avala only pushes this time.",
        [ForgeError.PushRejected] = "The repository was created and added as origin, but the forge refused the push.",
        [ForgeError.NoBaseBranch] = "There is nothing to push yet: the repository needs a commit on a branch.",
    }.ToFrozenDictionary();

    public static PersonalPhrase Published(PublishedRepository published) =>
        new("Published ", Named(published.Repository), $" on {published.Forge.Forge.Value} and pushed {published.Branch}; origin is ", Address(published.PushUrl), ".");

    public static PersonalPhrase Remote(RepositoryRemote remote) =>
        remote.Forge.Match(
            forge => new PersonalPhrase($"{remote.Remote} is ", remote.Address.Match(Named, () => Address(remote.Url)), $" on {forge.Forge.Value} ({forge.ForgeKind})."),
            () => new PersonalPhrase($"{remote.Remote} is ", Address(remote.Url), ", on no forge connection of forges.json."));

    public static PersonalPhrase Address(string url)
    {
        var bare = url.EndsWith(".git", StringComparison.Ordinal) ? url[..^4] : url;
        var name = bare.TrimEnd('/').LastIndexOf('/');
        var owner = name > 0 ? bare.LastIndexOfAny(['/', ':'], name - 1) : -1;

        return owner < 1 || name - owner < 2 || url[owner - 1] == '/'
            ? new PersonalPhrase(url)
            : new PersonalPhrase(url[..(owner + 1)], Personal.Login(url[(owner + 1)..name]), url[name..]);
    }

    public static string Failure(ForgeError error) =>
        Failures.TryGetValue(error, out var failure) ? failure : $"Publishing failed: {ForgePhrases.Error(error)}";

    private static PersonalPhrase Named(RepositoryAddress address) => new(Personal.Login(address.Owner), $"/{address.Name}");
}
