namespace Avala.Jobs.Domain;

internal readonly record struct JobId(Guid Value)
{
    public static JobId New() => new(Guid.CreateVersion7());
}
