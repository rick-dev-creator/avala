using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Verification.Checks;

internal sealed class CheckFileFormat : IRuleFileFormat
{
    public string Path => CheckDeclaration.RelativePath;

    public Option<RuleFileRejection> Rejection(string content) =>
        CheckDeclaration.Parse(content).Match(_ => Option<RuleFileRejection>.None, error => RuleFileRejection.Of("Verification", error));
}
