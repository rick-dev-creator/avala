using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Permissions.Policies;

internal sealed record AutomaticAnswer(FormAnswer Answer, IReadOnlyList<Assumption> Assumptions);

internal static class FormPolicy
{
    public const string Judgment = "Nobody is watching this job. Decide with your best judgment, state the assumption you make and go on.";

    public const string Refusal = "Nobody is watching this job, so permissions asked through a form are not granted. Stay within the worktree.";

    extension(PermissionPolicy policy)
    {
        public Option<AutomaticAnswer> Answer(ItemId item, AgentForm form) =>
            policy.Autonomy == Autonomy.Supervised ? Option<AutomaticAnswer>.None
            : form.Purpose == FormPurpose.Permission ? new AutomaticAnswer(new FormAnswer(item, []) { Declined = true, Message = Refusal }, [])
            : Answered(item, form, policy.Strategy);
    }

    private static AutomaticAnswer Answered(ItemId item, AgentForm form, FormStrategy strategy)
    {
        var fields = form.Fields.Select(field => Field(field, strategy)).ToList();

        return new AutomaticAnswer(new FormAnswer(item, [.. fields.Select(pair => pair.Answer)]), [.. fields.Select(pair => pair.Assumption)]);
    }

    private static (FieldAnswer Answer, Assumption Assumption) Field(FormField field, FormStrategy strategy)
    {
        var recommended = field.Options.FirstOrDefault(option => option.Recommended);
        var writes = field.Kind == FieldKind.FreeText || field.AcceptsFreeText;

        return field.Kind == FieldKind.Confirmation ? (new FieldAnswer(field.Id) { Confirmed = true }, Assumed(field, AssumptionBasis.Confirmed, []))
            : strategy == FormStrategy.Recommended && recommended is not null ? Chose(field, recommended, AssumptionBasis.RecommendedOption)
            : writes ? (new FieldAnswer(field.Id) { Text = Judgment }, Assumed(field, AssumptionBasis.AgentJudgment, []))
            : recommended is not null ? Chose(field, recommended, AssumptionBasis.RecommendedOption)
            : Chose(field, field.Options[0], AssumptionBasis.FirstOption);
    }

    private static (FieldAnswer Answer, Assumption Assumption) Chose(FormField field, FormOption option, AssumptionBasis basis) =>
        (new FieldAnswer(field.Id) { Chosen = [option.Label] }, Assumed(field, basis, [option.Label]));

    private static Assumption Assumed(FormField field, AssumptionBasis basis, IReadOnlyList<string> chosen) =>
        new(field.Id, field.Prompt, basis, chosen);
}
