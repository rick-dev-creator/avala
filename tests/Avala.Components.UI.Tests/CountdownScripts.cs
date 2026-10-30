using Avala.Components.UI.Theme;
using Avala.Testing.UI;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Components.UI.Tests;

public sealed class CountdownScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static (ViewScript View, Border Drain, Border Holder, Func<int> Runs) Counting(bool running)
    {
        var runs = 0;
        var drain = new Border { Name = "Drain", Height = 2, Width = 200 };
        Countdown.SetCommand(drain, new RelayCommand(() => runs++));
        Countdown.SetDuration(drain, TimeSpan.FromSeconds(6));
        Countdown.SetRunning(drain, running);
        var holder = new Border { Child = drain };
        var view = ViewScript.Show(holder, new object());

        return (view, drain, holder, () => runs);
    }

    [Fact]
    public Task ARunningCountdownRunsItsCommandOnceItsTimeIsUpAsync() =>
        ui.RunAsync(() =>
        {
            var (view, drain, _, runs) = Counting(running: true);

            view.Elapse(TimeSpan.FromSeconds(5));
            var early = (runs(), Countdown.IsCounting(drain), drain.Classes.Contains("counting"));
            view.Elapse(TimeSpan.FromSeconds(2));
            view.Elapse(TimeSpan.FromSeconds(1));

            Assert.Equal((0, true, true), early);
            Assert.Equal((1, false, false), (runs(), Countdown.IsCounting(drain), drain.Classes.Contains("counting")));
        }, Cancellation);

    [Fact]
    public Task StoppingACountdownBeforeItsTimeKeepsItsCommandFromRunningAsync() =>
        ui.RunAsync(() =>
        {
            var (view, drain, _, runs) = Counting(running: true);

            view.Elapse(TimeSpan.FromSeconds(3));
            Countdown.SetRunning(drain, false);
            view.Elapse(TimeSpan.FromSeconds(8));

            Assert.Equal((0, false), (runs(), Countdown.IsCounting(drain)));
        }, Cancellation);

    [Fact]
    public Task AHiddenCountdownWaitsAndStartsOverWhenItIsSeenAgainAsync() =>
        ui.RunAsync(() =>
        {
            var (view, drain, holder, runs) = Counting(running: true);

            view.Elapse(TimeSpan.FromSeconds(4));
            holder.IsVisible = false;
            view.Settle();
            view.Elapse(TimeSpan.FromSeconds(10));
            var hidden = (runs(), Countdown.IsCounting(drain));
            holder.IsVisible = true;
            view.Settle();
            view.Elapse(TimeSpan.FromSeconds(4));
            var restarted = runs();
            view.Elapse(TimeSpan.FromSeconds(3));
            view.Elapse(TimeSpan.FromSeconds(1));

            Assert.Equal((0, false), hidden);
            Assert.Equal(0, restarted);
            Assert.Equal(1, runs());
        }, Cancellation);

    [Fact]
    public Task ACountdownThatIsNotRunningNeverRunsItsCommandAsync() =>
        ui.RunAsync(() =>
        {
            var (view, drain, _, runs) = Counting(running: false);

            view.Elapse(TimeSpan.FromSeconds(10));

            Assert.Equal((0, false), (runs(), Countdown.IsCounting(drain)));
        }, Cancellation);
}
