namespace Avala.Sdk.Domain;

public interface IAggregateRoot<TId>
    where TId : struct
{
    TId Id { get; }
}
