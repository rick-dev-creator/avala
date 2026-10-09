using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Observability.Metrics;
using Avala.Observability.Tests.Metrics;
using Avala.Observability.Tracking;
using Avala.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Observability.Tests.Tracking;

internal sealed class Tracked : IDisposable
{
    public static readonly ProviderInfo Claude = new("claude", "Claude Code");
    public static readonly ProviderInfo Codex = new("codex", "Codex");

    private readonly UsageMeter meter = new();
    private readonly UsageTracker tracker;

    public Tracked()
    {
        Measurements = new MeterRecorder(meter.Meter);
        tracker = new UsageTracker(Book, meter, Clock, Bus);
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero));

    public UsageBook Book { get; } = new();

    public RecordingBus Bus { get; } = new();

    public MeterRecorder Measurements { get; }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async Task<SessionId> OpenAsync(ProviderInfo provider)
    {
        var session = SessionId.New();
        await tracker.HandleAsync(new SessionOpened(session, provider, "."), Cancellation);

        return session;
    }

    public async Task<SessionId> OpenAsync(ProviderInfo provider, JobId job)
    {
        var session = await OpenAsync(provider);
        await tracker.HandleAsync(new JobSessionStarted(job, session), Cancellation);

        return session;
    }

    public async Task SeeAsync(params IAgentEvent[] events)
    {
        foreach (var agentEvent in events)
        {
            await tracker.HandleAsync(new AgentActivity(agentEvent), Cancellation);
        }
    }

    public void Dispose()
    {
        Measurements.Dispose();
        meter.Dispose();
    }
}
