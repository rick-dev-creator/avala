using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;

namespace Avala.Simulator.Playback;

internal static class SimulatedCapabilities
{
    public const string WithoutSetting = "withoutCapabilities";

    public const string SurfacesSetting = "toolSurfaces";

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

    public static CapabilitySet On(ConnectionEnvironment connection)
    {
        var declared = connection.ApiKey.IsSome ? Declared : Declared.With(SubscriptionLimits);
        var surfaced = connection.Settings.TryGetValue(SurfacesSetting, out var surfaces)
            ? declared.With(new AcceptsTools([.. Names(surfaces).SelectMany(Surface)]))
            : declared;

        return connection.Settings.TryGetValue(WithoutSetting, out var without) ? Names(without).Aggregate(surfaced, Remove) : surfaced;
    }

    private static IEnumerable<string> Names(string listed) =>
        listed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => char.ToUpperInvariant(name[0]) + name[1..]);

    private static IEnumerable<ToolSurface> Surface(string name) =>
        Enum.TryParse<ToolSurface>(name, out var surface) ? [surface] : [];

    private static CapabilitySet Remove(CapabilitySet declared, string component) => component switch
    {
        nameof(StreamsPartialOutput) => declared.Without<StreamsPartialOutput>(),
        nameof(ExposesReasoning) => declared.Without<ExposesReasoning>(),
        nameof(Interruptible) => declared.Without<Interruptible>(),
        nameof(Resumable) => declared.Without<Resumable>(),
        nameof(AcceptsTools) => declared.Without<AcceptsTools>(),
        nameof(AsksForms) => declared.Without<AsksForms>(),
        nameof(ReportsUsage) => declared.Without<ReportsUsage>().Without<ReportsCost>(),
        nameof(ReportsCost) => declared.Without<ReportsCost>(),
        nameof(ReportsLimits) => declared.Without<ReportsLimits>(),
        _ => declared,
    };
}
