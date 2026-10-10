using Avala.Sdk;

namespace Avala.Agents.Contracts.Sessions;

public sealed record ModelChoice(Option<string> Model, Option<string> Effort)
{
    public static ModelChoice Default { get; } = new(Option<string>.None, Option<string>.None);

    public bool IsDefault => Model.IsNone && Effort.IsNone;

    public ModelChoice Or(ModelChoice fallback) =>
        new(Model.IsSome ? Model : fallback.Model, Effort.IsSome ? Effort : fallback.Effort);
}
