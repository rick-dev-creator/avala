namespace Avala.Jobs.Jobs;

internal readonly record struct AttemptNumber(int Value)
{
    public static AttemptNumber First { get; } = new(1);

    public AttemptNumber Next => new(Value + 1);
}
