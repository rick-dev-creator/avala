using Avala.Server.Activity.Domain;

namespace Avala.Server.Activity.Tests.Domain;

public sealed class RepositoryKeyTests
{
    public static TheoryData<string, string> Refused => new()
    {
        { "", "a repository key is required" },
        { "origin:github.com/rick/avala", "a repository key starts with 'root:' or 'remote:'" },
        { "remote:rick@github.com/rick/avala", "the repository key carries credentials" },
        { "remote:https://github.com/rick/avala", "the repository key carries a scheme" },
        { "remote:github.com:22/rick/avala", "the repository key carries a port" },
        { "remote:github.com/rick/av%61la", "the repository key is percent-encoded" },
        { "remote:github.com/rick/ava la", "the repository key contains whitespace" },
        { "remote:github.com/avala", "the repository key names a host and at least two path segments" },
        { "remote:github.com//rick/avala", "the repository key has an empty segment" },
        { "remote:localhost/rick/avala", "the repository key has an invalid host" },
        { "remote:github.com/rick/ava!la", "the repository key has an invalid path segment" },
        { "root:abc123", "a root key is the root commit's hash, 40 or 64 hexadecimal characters" },
    };

    [Fact]
    public void ARemoteKeyIsLowercased() =>
        Assert.Equal("remote:github.com/rick/avala", RepositoryKey.Create("remote:GitHub.com/Rick/Avala").Value.Value);

    [Fact]
    public void ATrailingDotGitIsRemoved() =>
        Assert.Equal("remote:github.com/rick/avala", RepositoryKey.Create("remote:github.com/rick/avala.git").Value.Value);

    [Fact]
    public void ATrailingSlashIsRemoved() =>
        Assert.Equal("remote:github.com/rick/avala", RepositoryKey.Create("remote:github.com/rick/avala/").Value.Value);

    [Fact]
    public void TwoFormsOfOneRemoteAreOneRepository() =>
        Assert.Equal(RepositoryKey.Create("remote:GitHub.com/Rick/Avala.git").Value, RepositoryKey.Create("remote:github.com/rick/avala/").Value);

    [Theory]
    [InlineData(40)]
    [InlineData(64)]
    public void ARootKeyIsFortyOrSixtyFourHexCharacters(int length) =>
        Assert.Equal($"root:{new string('a', length)}", RepositoryKey.Create($"ROOT:{new string('A', length)}").Value.Value);

    [Theory]
    [MemberData(nameof(Refused))]
    public void AKeyWithCredentialsAPortOrAnotherShapeIsRefused(string text, string reason) =>
        Assert.Equal(reason, RepositoryKey.Create(text).Refusal);

    [Fact]
    public void ARepositoryKeyLongerThanThreeHundredCharactersIsRefused() =>
        Assert.Equal(
            "the repository key is longer than 300 characters",
            RepositoryKey.Create($"remote:github.com/rick/{new string('a', 300)}").Refusal);
}
