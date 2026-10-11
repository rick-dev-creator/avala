using Avala.Notifications.Contracts;
using Avala.Notifications.Delivering;
using Avala.Notifications.Dispatching;
using Avala.Notifications.Notices;
using Avala.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Notifications.Tests;

internal sealed class Notified : IAsyncDisposable
{
    public static readonly NotificationSettings Ntfy = new(ChannelKind.Ntfy) { Ntfy = new NtfyTarget(NtfyTarget.DefaultServer, "avala-k3j9") };

    public Notified(NotificationSettings settings, MemoryStore store)
    {
        Clock = new FakeTimeProvider(Samples.Monday);
        Presence = new FakePresence(Clock);
        File = new FakeFile().With(settings);
        Store = store;
        Outlook = new Outlook(File, Presence, new FakeRedaction(), Clock);
        Courier = new Courier([Channel], Secrets);
        Dispatcher = new Dispatcher(Outlook, new SentLog(Store, Bus), Courier);
        Desk = new NotificationDesk(Dispatcher, Catalog, Clock);
    }

    public Notified(NotificationSettings settings)
        : this(settings, new MemoryStore())
    {
    }

    public Notified()
        : this(Ntfy)
    {
    }

    public static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public FakeTimeProvider Clock { get; }

    public FakePresence Presence { get; }

    public FakeFile File { get; }

    public MemoryStore Store { get; }

    public FakeChannel Channel { get; } = new();

    public FakeSecrets Secrets { get; } = new();

    public RecordingBus Bus { get; } = new();

    public FakeCatalog Catalog { get; } = new FakeCatalog().With(Samples.Job).With(Samples.Other);

    public Outlook Outlook { get; }

    public Courier Courier { get; }

    public Dispatcher Dispatcher { get; }

    public NotificationDesk Desk { get; }

    public IReadOnlyList<NotificationRecord> Records => [.. Bus.Published.OfType<NotificationSettled>().Select(settled => settled.Record)];

    public Task OfferAsync(Notice notice) => Dispatcher.OfferAsync(notice, Cancellation);

    public async Task AdvanceAsync(TimeSpan span)
    {
        Clock.Advance(span);
        await Dispatcher.DrainedAsync(Cancellation);
    }

    public async Task<NotificationRecord> SettledAsync(NotificationOutcome outcome) =>
        (await Bus.WaitForAsync<NotificationSettled>(settled => settled.Record.Outcome == outcome, Cancellation)).Record;

    public ValueTask DisposeAsync() => Dispatcher.DisposeAsync();
}
