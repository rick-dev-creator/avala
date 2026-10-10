using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Forges.RuleFiles;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Forges.Tests.RuleFiles;

public sealed class ParserTests
{
    [Fact]
    public void AForgesFileKeepsEveryConnectionWithItsForgeUrlAndCredentialReference()
    {
        var settings = Outcomes.Succeeds(ForgeFileParser.Parse("""
            {
              "pollSeconds": 120,
              "forges": [
                { "name": "github", "forge": "github", "credential": { "source": "env", "reference": "GITHUB_TOKEN" } },
                { "name": "github-cli", "forge": "github", "credential": { "source": "cli" } },
                { "name": "codeberg", "forge": "forgejo", "url": "https://codeberg.org" },
                { "name": "local", "forge": "simulated" }
              ]
            }
            """));

        Assert.Equal(TimeSpan.FromSeconds(120), settings.Poll);
        Assert.Equal(
            [
                new ForgeDeclaration(new ForgeName("github"), "github", Option<Uri>.None, CredentialSource.Environment, "GITHUB_TOKEN"),
                new ForgeDeclaration(new ForgeName("github-cli"), "github", Option<Uri>.None, CredentialSource.Cli, Option<string>.None),
                new ForgeDeclaration(new ForgeName("codeberg"), "forgejo", new Uri("https://codeberg.org"), CredentialSource.None, Option<string>.None),
                new ForgeDeclaration(new ForgeName("local"), "simulated", Option<Uri>.None, CredentialSource.None, Option<string>.None),
            ],
            settings.Forges);
    }

    [Fact]
    public void AnEmptyForgesFileHasNoConnectionsAndTheDefaultPoll() =>
        Assert.Equal((0, ForgeSettings.DefaultPoll), Outcomes.Succeeds(ForgeFileParser.Parse("{}")) is var settings ? (settings.Forges.Count, settings.Poll) : default);

    [Theory]
    [InlineData("not json", "Malformed")]
    [InlineData("[]", "Malformed")]
    [InlineData("""{ "forges": [], "theme": "dark" }""", "UnknownField")]
    [InlineData("""{ "forges": [{ "name": "a", "forge": "github", "token": "x" }] }""", "UnknownField")]
    [InlineData("""{ "forges": [{ "name": "a", "forge": "github" }, { "name": "a", "forge": "gitea", "url": "https://g.example" }] }""", "DuplicateName")]
    [InlineData("""{ "forges": [{ "name": "-a", "forge": "github" }] }""", "InvalidName")]
    [InlineData("""{ "forges": [{ "forge": "github" }] }""", "InvalidName")]
    [InlineData("""{ "forges": [{ "name": "a" }] }""", "MissingForge")]
    [InlineData("""{ "forges": [{ "name": "a", "forge": "gitea", "url": "ftp://g.example" }] }""", "InvalidUrl")]
    [InlineData("""{ "forges": [{ "name": "a", "forge": "github", "credential": { "source": "keychain" } }] }""", "UnknownSource")]
    [InlineData("""{ "forges": [{ "name": "a", "forge": "github", "credential": { "source": "env" } }] }""", "MissingReference")]
    [InlineData("""{ "forges": [{ "name": "a", "forge": "github", "credential": { "source": "env", "reference": " " } }] }""", "MissingReference")]
    [InlineData("""{ "pollSeconds": 5 }""", "InvalidInterval")]
    [InlineData("""{ "pollSeconds": 7200 }""", "InvalidInterval")]
    [InlineData("""{ "pollSeconds": "60" }""", "Malformed")]
    [InlineData("""{ "forges": [{ "name": "a", "name": "b", "forge": "github" }] }""", "Malformed")]
    public void AnInvalidForgesFileIsRejectedWithItsError(string text, string expected) =>
        Assert.Equal(Enum.Parse<ForgeError>(expected), Outcomes.FailsWith(ForgeFileParser.Parse(text)));

    [Fact]
    public void AForgesFileOverItsSizeLimitIsTooLarge() =>
        Assert.Equal(ForgeError.TooLarge, Outcomes.FailsWith(ForgeFileParser.Parse($$"""{ "forges": [], "pad": "{{new string('x', ForgeFileParser.MaximumBytes)}}" }""")));

    [Theory]
    [InlineData("""{ "pullRequest": { "forge": "github" } }""", "github", "origin", "WatchOnly", 3)]
    [InlineData("""{ "approval": "pull-request", "pullRequest": { "forge": "work", "remote": "upstream", "onPullRequest": "wake-on-ci", "maxWakeUps": 0 } }""", "work", "upstream", "WakeOnCi", 0)]
    [InlineData("""{ "pullRequest": { "forge": "work", "onPullRequest": "wake-on-ci-and-reviews", "maxWakeUps": 20 } }""", "work", "origin", "WakeOnCiAndReviews", 20)]
    public void ThePullRequestSectionNamesTheForgeRemotePolicyAndWakeUps(string text, string forge, string remote, string policy, int wakeUps) =>
        Assert.Equal(
            new PullRequestRules(new ForgeName(forge), remote, Enum.Parse<OnPullRequest>(policy), wakeUps),
            Outcomes.Present(Outcomes.Succeeds(PullRequestRulesParser.ParseJobFile(text))));

    [Theory]
    [InlineData("", "Automatic")]
    [InlineData(", \"redeliver\": \"automatic\"", "Automatic")]
    [InlineData(", \"redeliver\": \"review\"", "Review")]
    public void ThePullRequestSectionSaysWhetherAWokenFixIsRedeliveredOrReviewed(string field, string expected) =>
        Assert.Equal(
            Enum.Parse<Redelivery>(expected),
            Outcomes.Present(Outcomes.Succeeds(PullRequestRulesParser.ParseJobFile($$"""{ "pullRequest": { "forge": "github"{{field}} } }"""))).Redelivery);

    [Fact]
    public void AJobFileWithoutThePullRequestSectionDeclaresNone() =>
        Assert.True(Outcomes.Succeeds(PullRequestRulesParser.ParseJobFile("""{ "approval": "merge" }""")).IsNone);

    [Theory]
    [InlineData("""{ "pullRequest": { "remote": "origin" } }""", "MissingForge")]
    [InlineData("""{ "pullRequest": { "forge": "github", "onPullRequest": "wake-always" } }""", "UnknownPolicy")]
    [InlineData("""{ "pullRequest": { "forge": "github", "maxWakeUps": 21 } }""", "InvalidWakeUps")]
    [InlineData("""{ "pullRequest": { "forge": "github", "maxWakeUps": -1 } }""", "InvalidWakeUps")]
    [InlineData("""{ "pullRequest": { "forge": "github", "draft": true } }""", "UnknownField")]
    [InlineData("""{ "pullRequest": { "forge": "github", "redeliver": "never" } }""", "UnknownPolicy")]
    [InlineData("""{ "pullRequest": "github" }""", "Malformed")]
    [InlineData("""{ "pullRequest": { "forge": "" } }""", "Malformed")]
    public void AnInvalidPullRequestSectionIsRejectedWithItsError(string text, string expected) =>
        Assert.Equal(Enum.Parse<ForgeError>(expected), Outcomes.FailsWith(PullRequestRulesParser.ParseJobFile(text)));
}
