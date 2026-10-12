namespace Avala.Sdk.Appearance;

public interface IOnboarding
{
    ValueTask<bool> IsDoneAsync(CancellationToken cancellationToken);

    ValueTask<bool> FinishAsync(CancellationToken cancellationToken);
}
