namespace Avala.Jobs.Contracts;

public readonly record struct JobId(Guid Value)
{
    public static JobId New() => new(Guid.CreateVersion7());
}
