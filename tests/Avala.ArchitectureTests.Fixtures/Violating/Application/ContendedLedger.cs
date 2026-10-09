namespace Avala.Fixtures.Violating.Application;

public sealed class ContendedLedger
{
    private readonly Lock gate = new();
    private readonly SemaphoreSlim turn = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentQueue<decimal> postings = new();
    private decimal total;

    public decimal Total => total;

    public void Post(decimal amount)
    {
        lock (gate)
        {
            total += amount;
            postings.Enqueue(amount);
        }
    }

    public async Task PostLaterAsync(decimal amount)
    {
        await turn.WaitAsync();

        try
        {
            Post(amount);
        }
        finally
        {
            turn.Release();
        }
    }

    public bool IsBusy(object other) => System.Threading.Monitor.IsEntered(other);
}
