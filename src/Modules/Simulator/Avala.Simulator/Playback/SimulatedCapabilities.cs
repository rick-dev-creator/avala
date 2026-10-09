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

    private static readonly (string Name, Func<CapabilitySet, CapabilitySet> Remove)[] Removals =
    [
        (nameof(StreamsPartialOutput), declared => declared.Without<StreamsPartialOutput>()),
        (nameof(ExposesReasoning), declared => declared.Without<ExposesReasoning>()),
        (nameof(Interruptible), declared => declared.Without<Interruptible>()),
        (nameof(Resumable), declared => declared.Without<Resumable>()),
        (nameof(AcceptsTools), declared => declared.Without<AcceptsTools>()),
        (nameof(AsksForms), declared => declared.Without<AsksForms>()),
        (nameof(ReportsUsage), declared => declared.Without<ReportsUsage>().Without<ReportsCost>()),
        (nameof(ReportsCost), declared => declared.Without<ReportsCost>()),
        (nameof(ReportsLimits), declared => declared.Without<ReportsLimits>()),
    ];

    private static CapabilitySet Remove(CapabilitySet declared, string component) =>
        Removals.Where(removal => removal.Name == component).Aggregate(declared, (remaining, removal) => removal.Remove(remaining));
}
