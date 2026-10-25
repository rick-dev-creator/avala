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
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Resources.Tests;

internal sealed class FakeEndedJobs : IEndedJobs
{
    public HashSet<JobId> Working { get; } = [];

    public HashSet<JobId> Resting { get; } = [];

    public async ValueTask<bool> WhileRestingAsync(JobId job, Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        if (!Resting.Contains(job))
        {
            return false;
        }

        await work(cancellationToken);

        return true;
    }

    public async ValueTask<bool> WhileEndedAsync(JobId job, Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        if (Working.Contains(job))
        {
            return false;
        }

        await work(cancellationToken);

        return true;
    }
}

internal sealed class Resourced : IAsyncDisposable
{
    private readonly Option<TemporaryFolder> ownClaims;

    public Resourced(ResourceSettings settings, string claims)
    {
        Claims = claims;
        Settings = new FixedSettings(settings);
        Leases = Lessor.Leases(Bus, Listening, settings, claims);
        Book = new ResourceBook(Settings, Leases);
        Readings = new ProcessReadings(Trees, Listening);
        Reaper = new OrphanReaper(Readings, Settings, Bus, Clock);
        Housekeeper = new WorktreeHousekeeper(new RetainedWorktrees(Workspaces, Workspaces, Jobs, Facts), Settings, Bus, Clock);
        Tracker = new ResourceTracker(Book, Reaper, Leases, Housekeeper);
        Desk = new WorktreeDesk(Housekeeper, Reaper);
        Recovery = new RetentionRecovery(Catalog, Workspaces, Housekeeper, Reaper);
        Taker = new SampleTaker(Readings, Book, Folders, Bus);
    }

    public Resourced(ResourceSettings settings)
        : this(settings, new TemporaryFolder())
    {
    }

    public Resourced()
        : this(ResourceSettingsParser.Defaults)
    {
    }

    private Resourced(ResourceSettings settings, TemporaryFolder claims)
        : this(settings, claims.Path) => ownClaims = claims;

    public static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public RecordingBus Bus { get; } = new();

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero));

    public FakeTrees Trees { get; } = new();

    public FakeListening Listening { get; } = new();

    public FakeFolders Folders { get; } = new();

    public FakeWorkspaces Workspaces { get; } = new();

    public FakeEndedJobs Jobs { get; } = new();

    public FakeFacts Facts { get; } = new();

    public FixedSettings Settings { get; }

    public string Claims { get; }

    public PortLeases Leases { get; }

    public ResourceBook Book { get; }

    public ProcessReadings Readings { get; }

    public OrphanReaper Reaper { get; }

    public WorktreeHousekeeper Housekeeper { get; }

    public WorktreeDesk Desk { get; }

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
        await ownClaims.Match(folder => folder.DisposeAsync().AsTask(), () => Task.CompletedTask);
    }
}
