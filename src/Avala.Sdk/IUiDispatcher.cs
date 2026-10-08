namespace Avala.Sdk;

public interface IUiDispatcher
{
    ValueTask InvokeAsync(Action action, CancellationToken cancellationToken);
}
