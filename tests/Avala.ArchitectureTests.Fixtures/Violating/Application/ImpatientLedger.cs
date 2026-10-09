namespace Avala.Fixtures.Violating.Application;

public sealed class ImpatientLedger
{
    public void Settle() => Thread.Sleep(10);

    public Task SettleLaterAsync() => Task.Delay(TimeSpan.FromMilliseconds(10));

    public Task SettleOnTheClockAsync(TimeProvider clock) => System.Threading.Tasks.Task.Delay(TimeSpan.FromMilliseconds(10), clock);

    public void Spin() => Thread.SpinWait(10);

    public bool SpinUntil(Func<bool> condition) => SpinWait.SpinUntil(condition, 10);

    public IDisposable Tick(TimerCallback callback) => new Timer(callback, null, 10, 10);

    public IDisposable Elapse() => new System.Timers.Timer(10);

    public PeriodicTimer Every() => new(TimeSpan.FromMilliseconds(10));
}
