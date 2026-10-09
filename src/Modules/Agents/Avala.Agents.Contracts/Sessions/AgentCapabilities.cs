namespace Avala.Agents.Contracts.Sessions;

public sealed record AgentCapabilities(
    bool StreamsPartialOutput,
    bool ExposesReasoning,
    bool CanInterrupt,
    bool CanResume,
    bool AcceptsTools,
    bool ReportsUsage,
    bool ReportsCost,
    bool ReportsLimits);
