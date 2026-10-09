using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;

namespace Avala.Agents.Turns;

internal static class FormRules
{
    extension(AgentForm form)
    {
        public bool IsWellFormed() =>
            Enum.IsDefined(form.Purpose)
            && form.Fields.Count > 0
            && form.Fields.All(field => !string.IsNullOrWhiteSpace(field.Id) && WellFormed(field))
            && form.Fields.Select(field => field.Id).Distinct(StringComparer.Ordinal).Count() == form.Fields.Count;

        public bool Accepts(FormAnswer answer) =>
            answer.Declined
                ? answer.Fields.Count == 0
                : answer.Fields.Count == form.Fields.Count
                    && form.Fields.All(field => answer.Fields.Count(given => given.Field == field.Id) == 1
                        && Fits(field, answer.Fields.First(given => given.Field == field.Id)));
    }

    private static bool IsChoice(FormField field) => field.Kind is FieldKind.SingleChoice or FieldKind.MultipleChoice;

    private static bool WellFormed(FormField field) =>
        Enum.IsDefined(field.Kind)
        && (IsChoice(field) ? field.Options.Count > 0 : field.Options.Count == 0)
        && field.Options.Count(option => option.Recommended) <= 1
        && field.Options.All(option => !string.IsNullOrWhiteSpace(option.Label))
        && field.Options.Select(option => option.Label).Distinct(StringComparer.Ordinal).Count() == field.Options.Count;

    private static bool Fits(FormField field, FieldAnswer given)
    {
        var written = given.Text.Match(text => !string.IsNullOrWhiteSpace(text), () => false);
        var known = given.Chosen.All(label => field.Options.Any(option => option.Label == label))
            && given.Chosen.Distinct(StringComparer.Ordinal).Count() == given.Chosen.Count;
        var textFits = given.Text.IsNone || (written && (field.AcceptsFreeText || field.Kind == FieldKind.FreeText));

        return known && textFits && field.Kind switch
        {
            FieldKind.SingleChoice => given.Chosen.Count + (written ? 1 : 0) == 1,
            FieldKind.MultipleChoice => given.Chosen.Count > 0 || written,
            FieldKind.FreeText => given.Chosen.Count == 0 && written,
            _ => given.Chosen.Count == 0,
        };
    }
}
