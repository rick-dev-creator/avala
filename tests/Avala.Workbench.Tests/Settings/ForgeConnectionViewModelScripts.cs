using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Workbench.Settings;

namespace Avala.Workbench.Tests.Settings;

public sealed class ForgeConnectionViewModelScripts
{
    private static readonly ForgeInfo GitHub = new("github", "GitHub") { DefaultUrl = new Uri("https://api.github.com") };

    [Fact]
    public void AConnectionWithoutAUrlShowsItsForgesDefaultAndTheVariableNamingItsToken()
    {
        var forge = new ForgeConnectionViewModel(new ForgeConnectionInfo(new ForgeName("work"), "github", Option<Uri>.None, CredentialSource.Environment, "GITHUB_TOKEN"), [GitHub]);

        Assert.Equal(("work", "GitHub", "https://api.github.com/", "token in $GITHUB_TOKEN", string.Empty), (forge.Name, forge.Forge, forge.Url, forge.Credential, forge.Problem));
    }

    [Fact]
    public void AConnectionWhosePluginIsMissingSaysItIsNotUsable()
    {
        var forge = new ForgeConnectionViewModel(
            new ForgeConnectionInfo(new ForgeName("codeberg"), "forgejo", new Uri("https://codeberg.org"), CredentialSource.None, Option<string>.None) { Problem = ForgeError.UnknownForge },
            [GitHub]);

        Assert.Equal(("forgejo", "no credential", "Not usable: no plugin of that forge is installed."), (forge.Forge, forge.Credential, forge.Problem));
    }
}
