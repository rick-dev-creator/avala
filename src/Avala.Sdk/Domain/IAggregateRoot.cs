namespace Avala.Sdk.Domain;

public interface IAggregateRoot<out TId>
    where TId : struct
{
    TId Id { get; }
}
