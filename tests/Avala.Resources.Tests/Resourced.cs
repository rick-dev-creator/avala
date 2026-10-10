using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.Housekeeping;
using Avala.Resources.Leasing;
using Avala.Resources.Reaping;
using Avala.Resources.Sampling;
using Avala.Resources.Settings;
using Avala.Resources.Tracking;
using Avala.Sdk.Processes;
using Avala.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Resources.Tests;

internal sealed class Resourced : IAsyncDisposable
{
    public Resourced(ResourceSettings settings)
    {
        Settings = new FixedSettings(settings);
        Leases = Lessor.Leases(Bus, Listening, settings);
        Book = new ResourceBook(Settings, Leases);
        Readings = new ProcessReadings(Trees, Listening);
        Reaper = new OrphanReaper(Readings, Settings, Bus, Clock);
        Housekeeper = new WorktreeHousekeeper(Workspaces, Settings, Bus, Clock);
        Tracker = new ResourceTracker(Book, Reaper, Leases, Housekeeper);
        Recovery = new RetentionRecovery(Catalog, Workspaces, Housekeeper, Reaper);
        Taker = new SampleTaker(Readings, Book, Folders, Bus);
    }

    public Resourced()
        : this(ResourceSettingsParser.Defaults)
    {
    }

    public static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public RecordingBus Bus { get; } = new();

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero));

    public FakeTrees Trees { get; } = new();

    public FakeListening Listening { get; } = new();

    public FakeFolders Folders { get; } = new();

    public FakeWorkspaces Workspaces { get; } = new();

    public FixedSettings Settings { get; }

    public PortLeases Leases { get; }

    public ResourceBook Book { get; }

    public ProcessReadings Readings { get; }

    public OrphanReaper Reaper { get; }

    public WorktreeHousekeeper Housekeeper { get; }

    public ResourceTracker Tracker { get; }

    public FakeCatalog Catalog { get; } = new();

    public RetentionRecovery Recovery { get; }

    public SampleTaker Taker { get; }

    public static string Home(string name) => Path.Combine(Path.GetTempPath(), "avala-resources", name);

    public async Task<(SessionId Session, FakeTree Tree)> OpenAsync(JobId job, string home, string connection = "work", string provider = "simulator", params TreeProcess[] members)
    {
        var session = SessionId.New();
        var tree = Trees.Add(home, members);
        await Tracker.HandleAsync(
            new SessionOpened(session, new ProviderInfo(provider, provider), home, new ConnectionName(connection)) { ProcessTree = tree.Id },
            Cancellation);
        await Tracker.HandleAsync(new JobSessionStarted(job, session), Cancellation);

        return (session, tree);
    }

    public async Task<ResourceSample> SampleAsync(bool disks = true)
    {
        await Taker.TakeAsync(Clock.GetUtcNow(), disks, Cancellation);

        return Bus.Published.OfType<ResourcesSampled>().Last().Sample;
    }

    public async ValueTask DisposeAsync()
    {
        await Housekeeper.DisposeAsync();
        await Leases.DisposeAsync();
    }
}
