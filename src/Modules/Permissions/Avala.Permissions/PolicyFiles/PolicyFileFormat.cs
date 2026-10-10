using System.Text;
using Avala.Permissions.Contracts;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Permissions.PolicyFiles;

internal sealed class PolicyFileFormat : IRuleFileFormat
{
    public string Path => PermissionPolicy.PolicyFile;

    public static Option<PolicyError> Problem(string content) =>
        Encoding.UTF8.GetByteCount(content) > PolicyFileReader.MaximumBytes
            ? PolicyError.TooLarge
            : PolicyFileParser.Parse(content).Match(_ => Option<PolicyError>.None, Option<PolicyError>.Some);

    public Option<RuleFileRejection> Rejection(string content) =>
        Problem(content).Bind(error => RuleFileRejection.Of("Permissions", error).ToOption());
}
