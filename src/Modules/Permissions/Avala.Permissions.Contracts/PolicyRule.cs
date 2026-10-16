using Avala.Agents.Contracts.Events;
using Avala.Sdk;

namespace Avala.Permissions.Contracts;

public enum PolicyAnswer
{
    Allow,
    Deny,
    Ask,
}

public enum RuleOrigin
{
    BuiltIn,
    Repository,
    Session,
    Job,
}

public enum RuleScope
{
    Anywhere,
    Workspace,
    OutsideWorkspace,
    ReadOnly,
    Readable,
}

public enum FormStrategy
{
    Recommended,
    BestJudgment,
}

public sealed record PolicyRule(
    RuleOrigin Origin,
    string Name,
    Option<ItemKind> Kind,
    Option<string> Target,
    RuleScope Scope,
    PolicyAnswer Answer)
{
    public bool AnyArguments { get; init; }
}
