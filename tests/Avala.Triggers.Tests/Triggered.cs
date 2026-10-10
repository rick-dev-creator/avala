using Avala.Autopilot.Contracts;
using Avala.Permissions.Contracts;
using Avala.Testing;
using Avala.Triggers.Catalog;
using Avala.Triggers.Contracts;
using Avala.Triggers.Firing;
using Avala.Triggers.Looping;
using Avala.Triggers.Receiving;
using Avala.Triggers.Records;
using Avala.Triggers.Scheduling;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Triggers.Tests;

internal sealed class Triggered : IAsyncDisposable
{
    public const string Repository = "/work/ledger-api";

    public static readonly DateTimeOffset Monday = new(2026, 10, 5, 6, 0, 0, TimeSpan.Zero);

    private Triggered(Shared shared)
    {
        World = shared;
        Catalog = new TriggerCatalog(shared.Files);
        Book = new ScheduleBook(shared.Store);
        Journal = new RunJournal(shared.Store, shared.Bus, shared.Clock);
        Queue = new LoopQueue();
        Source = new TriggerTaskSource(Queue, Journal);
        Checks = new FiringChecks(shared.Jobs, [shared.Policies], Journal, Queue);
        Firer = new TriggerFirer(Catalog, Checks, new TriggerDispatch(shared.Jobs, [shared.Autopilot], Queue), Journal);
        Scheduler = new TriggerScheduler(Catalog, Book, Firer, Journal);
        Gate = new WebhookGate(shared.Secrets, Journal);
        Desk = new WebhookDesk(Catalog, Book, Gate, Firer);
        Triggers = new TriggerBook(Scheduler, Firer, Checks, new FakeEndpoint());
    }

    public Shared World { get; }

    public FakeTimeProvider Clock => World.Clock;

    public RecordingBus Bus => World.Bus;

    public FakeJobs Jobs => World.Jobs;

    public TriggerCatalog Catalog { get; }

    public ScheduleBook Book { get; }

    public RunJournal Journal { get; }

    public LoopQueue Queue { get; }

    public IJobSource Source { get; }

    public FiringChecks Checks { get; }

    public TriggerFirer Firer { get; }

    public TriggerScheduler Scheduler { get; }

    public WebhookGate Gate { get; }

    public WebhookDesk Desk { get; }

    public ITriggers Triggers { get; }

    public static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static async Task<Triggered> StartAsync(string machine, Action<Shared>? arrange = null)
    {
        var shared = new Shared();
        shared.Files.Machine = machine;
        arrange?.Invoke(shared);

        return await StartedAsync(shared);
    }

    public async Task<Triggered> RestartAsync(TimeSpan closed)
    {
        await DisposeAsync();
        Clock.Advance(closed);

        return await StartedAsync(World);
    }

    public async Task<TriggerRun> FiredAsync(Func<TriggerRun, bool> match) =>
        (await Bus.WaitForAsync<TriggerFired>(fired => match(fired.Run), Cancellation)).Run;

    public async ValueTask DisposeAsync()
    {
        await Scheduler.DisposeAsync();
        await Desk.DisposeAsync();
        await Firer.DisposeAsync();
    }

    private static async Task<Triggered> StartedAsync(Shared shared)
    {
        var triggered = new Triggered(shared);
        await triggered.Book.RunAsync(Cancellation);
        await triggered.Journal.RunAsync(Cancellation);
        await triggered.Gate.RunAsync(Cancellation);
        await triggered.Scheduler.RunAsync(Cancellation);

        return triggered;
    }

    internal sealed class Shared
    {
        public Shared()
        {
            Clock = new FakeTimeProvider(Monday);
            Clock.SetLocalTimeZone(Zones.Madrid);
        }

        public FakeTimeProvider Clock { get; }

        public RecordingBus Bus { get; } = new();

        public FakeTriggerFiles Files { get; } = new();

        public MemoryTriggerStore Store { get; } = new();

        public FakeJobs Jobs { get; } = new();

        public FakePolicies Policies { get; } = new();

        public FakeAutopilot Autopilot { get; } = new();

        public FakeSecrets Secrets { get; } = new();
    }
}

internal static class Zones
{
    public static TimeZoneInfo Madrid { get; } = TimeZoneInfo.CreateCustomTimeZone(
        "Test/Madrid",
        TimeSpan.FromHours(1),
        "Test/Madrid",
        "CET",
        "CEST",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
                new DateTime(2099, 12, 31, 0, 0, 0, DateTimeKind.Unspecified),
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0, DateTimeKind.Unspecified), 3, 5, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0, DateTimeKind.Unspecified), 10, 5, DayOfWeek.Sunday)),
        ]);
}

internal static class Declared
{
    public static string Machine(string triggers, string repositories = "[]") =>
        $$"""{ "repositories": {{repositories}}, "triggers": [ {{triggers}} ] }""";

    public static string Trigger(string fields) =>
        $$"""{ "id": "nightly", "repository": "{{Triggered.Repository}}", "instruction": "Update the dependencies", {{fields}} }""";
}
