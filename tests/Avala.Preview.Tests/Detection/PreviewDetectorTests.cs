using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Preview.Detection;
using Avala.Preview.Running;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;

namespace Avala.Preview.Tests.Detection;

public sealed class PreviewDetectorTests
{
    private readonly RecordingBus bus = new();
    private readonly PreviewBook book = new();
    private readonly JobId job = JobId.New();
    private readonly ProcessTreeId tree = new(Guid.NewGuid());
    private readonly PreviewDetector detector;

    public PreviewDetectorTests() => detector = new PreviewDetector(new NoLeases(), book, new RunBook(new RecordingBus()), bus);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EachPortAJobOpensIsAnnouncedOnceAndEachPortItClosesIsGoneAsync()
    {
        await SampleAsync(5173);
        await SampleAsync(5173, 6006);
        await SampleAsync(6006);

        Assert.Equal(
            [("available", 5173), ("available", 6006), ("gone", 5173)],
            bus.Published.Select(published => published switch
            {
                PreviewAvailable available => ("available", available.Target.Address.Port),
                PreviewGone gone => ("gone", gone.Target.Address.Port),
                _ => ("other", 0),
            }));
    }

    private ValueTask SampleAsync(params int[] ports) =>
        detector.HandleAsync(
            new ResourcesSampled(new ResourceSample(
                DateTimeOffset.UnixEpoch,
                [new TreeUsage(tree, "/worktrees/1", [new ProcessUsage(41, "node", 1024, TimeSpan.Zero, ports)], 0) { Job = job }],
                [],
                0)),
            Cancellation);

    private sealed class NoLeases : IResources
    {
        public Option<ResourceSample> Latest => Option<ResourceSample>.None;

        public ResourceUsage Global() => throw new NotSupportedException();

        public ResourceUsage OfJob(JobId job) => throw new NotSupportedException();

        public ResourceUsage OfSession(SessionId session) => throw new NotSupportedException();

        public IReadOnlyList<ConnectionResources> ByConnection() => [];

        public IReadOnlyList<ProviderResources> ByProvider() => [];

        public IReadOnlyList<PortLease> Leases() => [];

        public IReadOnlyList<PortConflict> Conflicts() => [];

        public ValueTask<ResourceSettings> SettingsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<ResourceSettings, ResourceError>> ChangeWorktreesAsync(WorktreePolicy policy, ReconcilePolicy reconcile, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
