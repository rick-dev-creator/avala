using Avala.Sdk;

namespace Avala.Fleet.Facts;

internal enum FleetError
{
    InvalidAddress,
    InsecureAddress,
    NotAKey,
    TokensOutOfBounds,
}

internal static class FactKinds
{
    public const string JobSubmitted = "jobSubmitted";

    public const string JobModelChosen = "jobModelChosen";

    public const string JobProgressed = "jobProgressed";

    public const string JobHeld = "jobHeld";

    public const string UsageReported = "usageReported";

    public const string AttemptVerified = "attemptVerified";
}

internal interface IFactBody
{
    string Kind { get; }
}

internal sealed record Fact(Guid Id, DateTimeOffset OccurredAt, IFactBody Body);

internal sealed record FactTokens(long Input, long Output, long CacheRead, long CacheWrite, long Reasoning);

internal sealed record FactCost(decimal Amount, string Currency, string Basis);

internal sealed record JobSubmittedFact(Guid Job, string Repository, Option<Guid> Parent, Option<string> Model, Option<string> Effort) : IFactBody
{
    public string Kind => FactKinds.JobSubmitted;
}

internal sealed record JobModelChosenFact(Guid Job, Option<string> Model, Option<string> Effort) : IFactBody
{
    public string Kind => FactKinds.JobModelChosen;
}

internal sealed record JobProgressedFact(Guid Job, string Status) : IFactBody
{
    public string Kind => FactKinds.JobProgressed;
}

internal sealed record JobHeldFact(Guid Job, string Reason) : IFactBody
{
    public string Kind => FactKinds.JobHeld;
}

internal sealed record UsageReportedFact(Option<Guid> Job, FactTokens Tokens, Option<FactCost> Cost, Option<string> Model) : IFactBody
{
    public string Kind => FactKinds.UsageReported;
}

internal sealed record AttemptVerifiedFact(Guid Job, int Attempt, string Outcome) : IFactBody
{
    public string Kind => FactKinds.AttemptVerified;
}
