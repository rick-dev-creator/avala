using Avala.Sdk;

namespace Avala.Jobs.Jobs;

internal sealed record Instruction
{
    private Instruction(string text) => Text = text;

    public string Text { get; }

    public static Result<Instruction, JobError> Create(string text) =>
        string.IsNullOrWhiteSpace(text) ? JobError.EmptyInstruction : new Instruction(text.Trim());
}
