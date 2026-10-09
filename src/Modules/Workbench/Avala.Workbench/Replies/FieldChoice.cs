using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Workbench.Replies;

internal sealed record FieldChoice(FormField Field, IReadOnlyList<string> Chosen, string Text, bool Confirmed)
{
    public static FieldChoice Recommended(FormField field) =>
        new(field, [.. field.Options.Where(option => option.Recommended).Select(option => option.Label)], string.Empty, false);

    public bool IsComplete => Field.Kind switch
    {
        FieldKind.SingleChoice => Chosen.Count == 1 || (Field.AcceptsFreeText && HasText),
        FieldKind.MultipleChoice => Chosen.Count > 0 || (Field.AcceptsFreeText && HasText),
        FieldKind.FreeText => HasText,
        _ => true,
    };

    public FieldAnswer Answer() => Field.Kind switch
    {
        FieldKind.SingleChoice when Field.AcceptsFreeText && HasText => new FieldAnswer(Field.Id) { Text = Text.Trim() },
        FieldKind.SingleChoice => new FieldAnswer(Field.Id) { Chosen = Chosen },
        FieldKind.FreeText => new FieldAnswer(Field.Id) { Text = Text.Trim() },
        FieldKind.Confirmation => new FieldAnswer(Field.Id) { Confirmed = Confirmed, Text = Typed },
        _ => new FieldAnswer(Field.Id) { Chosen = Chosen, Text = Typed },
    };

    private bool HasText => !string.IsNullOrWhiteSpace(Text);

    private Option<string> Typed => Field.AcceptsFreeText && HasText ? Text.Trim() : Option<string>.None;
}

internal static class FormReplies
{
    public static bool IsComplete(IReadOnlyList<FieldChoice> choices) => choices.All(choice => choice.IsComplete);

    public static FormAnswer Answer(ItemId item, IReadOnlyList<FieldChoice> choices) =>
        new(item, [.. choices.Select(choice => choice.Answer())]);

    public static FormAnswer Decline(ItemId item, string message) =>
        new(item, [])
        {
            Declined = true,
            Message = string.IsNullOrWhiteSpace(message) ? Option<string>.None : message.Trim(),
        };
}
