using Avala.Testing;
using Avala.Verification.Checks;

namespace Avala.Verification.Tests.Checks;

public sealed class CheckDeclarationTests
{
    [Fact]
    public void ADeclarationListsItsChecksInOrderWithANameAndATimeoutByDefault()
    {
        const string Declaration = """
            {
              // Comments and trailing commas are tolerated.
              "checks": [
                { "name": "build", "command": "dotnet", "arguments": ["build", "--no-restore"], "timeoutSeconds": 90 },
                { "command": "git", "arguments": ["grep", "add(2, 2) = 4"] },
              ]
            }
            """;

        var checks = Outcomes.Succeeds(CheckDeclaration.Parse(Declaration));

        Assert.Equal(
            [
                ("build", "dotnet", "build --no-restore", TimeSpan.FromSeconds(90)),
                ("git grep \"add(2, 2) = 4\"", "git", "grep add(2, 2) = 4", TimeSpan.FromMinutes(10)),
            ],
            checks.Select(check => (check.Name, check.Command, string.Join(' ', check.Arguments), check.Timeout)));
    }

    [Theory]
    [InlineData("not json", nameof(VerificationError.MalformedDeclaration))]
    [InlineData("[]", nameof(VerificationError.MalformedDeclaration))]
    [InlineData("""{ "checks": {} }""", nameof(VerificationError.MalformedDeclaration))]
    [InlineData("""{ "checks": ["dotnet build"] }""", nameof(VerificationError.MalformedDeclaration))]
    [InlineData("""{ "checks": [{ "command": "dotnet", "arguments": "build" }] }""", nameof(VerificationError.MalformedDeclaration))]
    [InlineData("""{ "checks": [{ "command": "dotnet", "name": 7 }] }""", nameof(VerificationError.MalformedDeclaration))]
    [InlineData("""{ "checks": [{ "arguments": ["build"] }] }""", nameof(VerificationError.MissingCommand))]
    [InlineData("""{ "checks": [{ "command": " " }] }""", nameof(VerificationError.MissingCommand))]
    [InlineData("""{ "checks": [{ "command": "dotnet", "timeoutSeconds": 0 }] }""", nameof(VerificationError.InvalidTimeout))]
    [InlineData("""{ "checks": [{ "command": "dotnet", "timeoutSeconds": 86401 }] }""", nameof(VerificationError.InvalidTimeout))]
    [InlineData("""{ "checks": [{ "command": "dotnet", "timeoutSeconds": "60" }] }""", nameof(VerificationError.InvalidTimeout))]
    public void AnInvalidDeclarationIsRejectedWithItsProblem(string declaration, string expected) =>
        Assert.Equal(expected, Outcomes.FailsWith(CheckDeclaration.Parse(declaration)).ToString());
}
