namespace Avala.Agents.Contracts.Events;

public enum FormPurpose
{
    Permission,
    Question,
    PlanApproval,
    Other,
}

public enum FieldKind
{
    SingleChoice,
    MultipleChoice,
    FreeText,
    Confirmation,
}

public sealed record FormOption(string Label, string Description, bool Recommended = false);

public sealed record FormField(string Id, string Header, string Prompt, FieldKind Kind, IReadOnlyList<FormOption> Options, bool AcceptsFreeText = false);

public sealed record AgentForm(FormPurpose Purpose, string Title, string Context, IReadOnlyList<FormField> Fields);
