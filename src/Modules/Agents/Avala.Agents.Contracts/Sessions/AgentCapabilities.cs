namespace Avala.Agents.Contracts.Sessions;

public sealed record AgentCapabilities(
    bool StreamsPartialOutput,
    bool ExposesReasoning,
    bool CanInterrupt,
    bool CanResume,
    bool ReportsUsage,
    bool ReportsCost,
    bool ReportsLimits);
