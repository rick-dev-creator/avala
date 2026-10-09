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
}

public enum RuleScope
{
    Anywhere,
    Workspace,
    OutsideWorkspace,
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
    PolicyAnswer Answer);
