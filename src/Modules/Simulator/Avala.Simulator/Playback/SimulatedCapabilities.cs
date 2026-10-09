using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;

namespace Avala.Simulator.Playback;

internal static class SimulatedCapabilities
{
    private static readonly CapabilitySet Declared = CapabilitySet.Of(
        new StreamsPartialOutput(),
        new ExposesReasoning(),
        new Interruptible(),
        new Resumable(),
        new AcceptsTools([ToolSurface.Canvas, ToolSurface.Executed]),
        new AsksForms(),
        new ReportsUsage(),
        new ReportsCost("USD"));

    public static ReportsLimits SubscriptionLimits { get; } = new(["5h", "7d", "7d opus", "7d sonnet"]);

    public static CapabilitySet On(ConnectionEnvironment connection) =>
        connection.ApiKey.IsSome ? Declared : Declared.With(SubscriptionLimits);

    public static bool Reports(this CapabilitySet declared, IAgentEvent cue) =>
        cue is not LimitReported || declared.Has<ReportsLimits>();
}
