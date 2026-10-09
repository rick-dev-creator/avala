using Avala.Agents.Contracts.Events;
using Avala.Permissions.Contracts;
using Avala.Permissions.PolicyFiles;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Permissions.Tests.PolicyFiles;

public sealed class PolicyFileParserTests
{
    [Fact]
    public void AValidFileBecomesOrderedRepositoryRulesNamedByPositionWhenUnnamed()
    {
        const string File = """
            {
              "rules": [
                { "name": "run the tests", "kind": "command", "target": "dotnet test*", "answer": "allow" },
                { "kind": "Web", "answer": "deny" },
                { "kind": "fileEdit", "within": "workspace", "target": "docs/*", "answer": "ask" }
              ]
            }
            """;

        var rules = Outcomes.Succeeds(PolicyFileParser.Parse(File));

        Assert.Equal(
            [
                new PolicyRule(RuleOrigin.Repository, "run the tests", ItemKind.Command, "dotnet test*", RuleScope.Anywhere, PolicyAnswer.Allow),
                new PolicyRule(RuleOrigin.Repository, "rule 2", ItemKind.Web, Option<string>.None, RuleScope.Anywhere, PolicyAnswer.Deny),
                new PolicyRule(RuleOrigin.Repository, "rule 3", ItemKind.FileEdit, "docs/*", RuleScope.Workspace, PolicyAnswer.Ask),
            ],
            rules);
    }

    [Theory]
    [InlineData("not json", PolicyError.Malformed)]
    [InlineData("""{ "rules": { } }""", PolicyError.Malformed)]
    [InlineData("""{ "rules": [ "allow" ] }""", PolicyError.Malformed)]
    [InlineData("""{ "rules": [ { "answer": true } ] }""", PolicyError.Malformed)]
    [InlineData("""{ "rules": [ { "answer": "allow", "answer": "deny" } ] }""", PolicyError.Malformed)]
    [InlineData("""{ "rules": [ { "answer": [ [ [ "allow" ] ] ] } ] }""", PolicyError.Malformed)]
    [InlineData("""{ "rules": [], "mode": "yolo" }""", PolicyError.UnknownField)]
    [InlineData("""{ "rules": [ { "answer": "allow", "when": "always" } ] }""", PolicyError.UnknownField)]
    [InlineData("""{ "rules": [ { "kind": "shell", "answer": "allow" } ] }""", PolicyError.UnknownKind)]
    [InlineData("""{ "rules": [ { "kind": "3", "answer": "allow" } ] }""", PolicyError.UnknownKind)]
    [InlineData("""{ "rules": [ { "within": "home", "answer": "allow" } ] }""", PolicyError.UnknownScope)]
    [InlineData("""{ "rules": [ { "kind": "command" } ] }""", PolicyError.MissingAnswer)]
    [InlineData("""{ "rules": [ { "answer": "yes" } ] }""", PolicyError.UnknownAnswer)]
    [InlineData("""{ "rules": [ { "kind": "command", "within": "workspace", "answer": "allow" } ] }""", PolicyError.ScopeNeedsFileEdits)]
    public void AnInvalidFileIsRejectedWithItsError(string file, PolicyError expected) =>
        Assert.Equal(expected, Outcomes.FailsWith(PolicyFileParser.Parse(file)));
}
