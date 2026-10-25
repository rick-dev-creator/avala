namespace Avala.Server.Activity.Domain;

internal sealed record ModelChoice(string? Model, string? Effort)
{
    public const int ModelLength = 100;

    public const int EffortLength = 20;

    public static Admission<ModelChoice> Create(string? model, string? effort) =>
        model?.Length > ModelLength ? Admission.Refuse<ModelChoice>($"a model name has at most {ModelLength} characters")
        : effort?.Length > EffortLength ? Admission.Refuse<ModelChoice>($"an effort has at most {EffortLength} characters")
        : !StoredText.IsStorable(model ?? string.Empty) || !StoredText.IsStorable(effort ?? string.Empty)
            ? Admission.Refuse<ModelChoice>("a model name or effort holds a control character or broken text")
        : Admission.Admit(new ModelChoice(Blank(model), Blank(effort)));

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
