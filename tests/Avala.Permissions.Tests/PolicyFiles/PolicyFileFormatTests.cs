using Avala.Permissions.Contracts;
using Avala.Permissions.PolicyFiles;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Permissions.Tests.PolicyFiles;

public sealed class PolicyFileFormatTests
{
    public static TheoryData<string, PolicyError?> Files => new()
    {
        { """{ "rules": [ { "kind": "command", "answer": "allow" } ] }""", null },
        { "not json", PolicyError.Malformed },
        { $$"""{ "rules": [], "note": "{{new string('x', PolicyFileReader.MaximumBytes)}}" }""", PolicyError.TooLarge },
    };

    [Theory]
    [MemberData(nameof(Files))]
    public void AnEditedPolicyFileIsAcceptedOrRejectedForPermissionsWithItsError(string content, PolicyError? error)
    {
        var format = new PolicyFileFormat();

        Assert.Equal(".avala/permissions.json", format.Path);
        Assert.Equal(
            error is { } rejected ? Option<RuleFileRejection>.Some(new RuleFileRejection("Permissions", rejected)) : Option<RuleFileRejection>.None,
            format.Rejection(content));
    }
}
