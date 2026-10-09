using Avala.Agents.Contracts.Sessions;

namespace Avala.Agents.Contracts.Capabilities;

public sealed record StreamsPartialOutput : ICapability;

public sealed record ExposesReasoning : ICapability;

public sealed record Interruptible : ICapability;

public sealed record Resumable : ICapability;

public sealed record AcceptsTools(ValueSet<ToolSurface> Surfaces) : ICapability
{
    public AcceptsTools(ToolSurface[] surfaces)
        : this(new ValueSet<ToolSurface>(surfaces))
    {
    }

    public bool Accepts(ToolSurface surface) => Surfaces.Contains(surface);
}

public sealed record AsksForms : ICapability;

public sealed record AcceptsMessagesMidTurn : ICapability;

public sealed record ReportsUsage : ICapability;

public sealed record ReportsCost(string Currency) : ICapability;

public sealed record ReportsLimits(ValueSet<string> Windows) : ICapability
{
    public ReportsLimits(string[] windows)
        : this(new ValueSet<string>(windows))
    {
    }

    public bool Covers(string window) => Windows.Contains(window);
}
