using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Testing;

namespace Avala.Forges.Tests.Policy;

public sealed class RemoteAddressesTests
{
    [Theory]
    [InlineData("https://github.com/octo/shop.git", "github.com", "octo", "shop")]
    [InlineData("https://github.com/octo/shop", "github.com", "octo", "shop")]
    [InlineData("git@github.com:octo/shop.git", "github.com", "octo", "shop")]
    [InlineData("ssh://git@codeberg.org:2222/octo/shop.git", "codeberg.org", "octo", "shop")]
    [InlineData("https://git.example.com/gitea/octo/shop.git", "git.example.com", "octo", "shop")]
    [InlineData("/srv/remotes/octo/shop.git", "", "octo", "shop")]
    [InlineData("C:\\remotes\\octo\\shop.git", "", "octo", "shop")]
    [InlineData("file:///srv/remotes/octo/shop.git", "", "octo", "shop")]
    public void ARemoteUrlGivesItsHostOwnerAndName(string remote, string host, string owner, string name) =>
        Assert.Equal(new RepositoryAddress(host, owner, name), Outcomes.Present(RemoteAddresses.Parse(remote)));

    [Theory]
    [InlineData("")]
    [InlineData("https://github.com/shop")]
    [InlineData("shop")]
    [InlineData("git@github.com:shop.git")]
    public void ARemoteWithoutOwnerAndNameIsRefused(string remote) =>
        Assert.True(RemoteAddresses.Parse(remote).IsNone);
}
