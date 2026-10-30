using System.Globalization;
using System.Runtime.InteropServices;
using Avala.Host.Composition;
using Avala.Runtime.Diagnostics;
using Avala.Sdk;
using Avala.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Host.Tests;

public sealed class StopRecordTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASignalToEndIsLoggedAndStopsAvalaInOrderInsteadOfKillingItAsync()
    {
        await using var data = new TemporaryFolder();
        var log = Logged(data, new FakeTimeProvider());
        var stops = 0;
        var signals = new ExitSignals(new CrashLog(log), () => stops++);
        var context = new PosixSignalContext(PosixSignal.SIGTERM);

        signals.Receive(context);
        await log.DisposeAsync();

        Assert.Equal((true, 1), (context.Cancel, stops));
        Assert.Equal(
            [("Warning", "Avala", "Avala received SIGTERM and stops its agent sessions in order")],
            (await ReadAsync(data)).Select(entry => (entry.Level, entry.Category, entry.Message)));
    }

    [Fact]
    public async Task AProcessExitWithoutAnOrderlyStopIsLoggedAsync()
    {
        await using var data = new TemporaryFolder();
        var log = Logged(data, new FakeTimeProvider());

        new CrashLog(log).Exiting();
        await log.DisposeAsync();

        Assert.Equal(
            [("Warning", "Avala", "Avala's process is exiting without stopping its agent sessions")],
            (await ReadAsync(data)).Select(entry => (entry.Level, entry.Category, entry.Message)));
    }

    [Fact]
    public async Task VitalSignsAreLoggedEveryMinuteWithMemoryThreadsAndTheInterfacesWaitAsync()
    {
        await using var data = new TemporaryFolder();
        var clock = new FakeTimeProvider();
        var log = Logged(data, clock);
        using var timer = new VitalSigns(log, InlineUiDispatcher.Instance, clock).Follow(Cancellation);

        clock.Advance(VitalSigns.Every);
        clock.Advance(VitalSigns.Every);
        await log.DisposeAsync();
        var vitals = await ReadAsync(data);

        Assert.Equal([("Information", "Avala.Vitals"), ("Information", "Avala.Vitals")], vitals.Select(entry => (entry.Level, entry.Category)));
        Assert.All(vitals, entry => Assert.True(
            Positive(entry.Property("WorkingSet")) && Positive(entry.Property("Heap")) && Positive(entry.Property("Threads")) && entry.Property("InterfaceWait") == "0",
            entry.Message));
    }

    [Fact]
    public async Task AnInterfaceThatStopsAnsweringShowsHowLongItHasMadeAvalaWaitAsync()
    {
        await using var data = new TemporaryFolder();
        var clock = new FakeTimeProvider();
        var log = Logged(data, clock);
        using var timer = new VitalSigns(log, new Frozen(), clock).Follow(Cancellation);

        clock.Advance(VitalSigns.Every);
        clock.Advance(VitalSigns.Every);
        clock.Advance(VitalSigns.Every);
        await log.DisposeAsync();

        Assert.Equal(["0", "60", "120"], (await ReadAsync(data)).Select(entry => entry.Property("InterfaceWait")));
    }

    private static bool Positive(string value) => long.Parse(value, CultureInfo.InvariantCulture) > 0;

    private static LogFile Logged(TemporaryFolder data, TimeProvider clock) =>
        new(data.Path, clock, LogRedaction.From(new Dictionary<string, string>()), LogFile.Limit);

    private static async Task<IReadOnlyList<LogEvent>> ReadAsync(TemporaryFolder data) =>
        LogEvents.Parse(await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(data.Path)), Cancellation));

    private sealed class Frozen : IUiDispatcher
    {
        private readonly TaskCompletionSource never = new();

        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken) => new(never.Task);
    }
}
