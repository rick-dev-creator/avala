namespace Avala.Sdk;

public static class Cancellations
{
    extension(CancellationToken cancellationToken)
    {
        public Task UntilCancelledAsync() => new TaskCompletionSource().Task.WaitAsync(cancellationToken);
    }
}
