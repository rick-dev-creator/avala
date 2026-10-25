namespace Avala.Server.Activity.Domain;

internal sealed class FleetJob
{
    private readonly List<CostTotal> costs = [];

    private FleetJob(Guid instanceId, Guid jobId, DateTimeOffset firstActivityAt)
    {
        InstanceId = instanceId;
        JobId = jobId;
        FirstActivityAt = firstActivityAt;
        LastActivityAt = firstActivityAt;
    }

    public Guid InstanceId { get; }

    public Guid JobId { get; }

    public RepositoryKey? Repository { get; private set; }

    public Guid? ParentId { get; private set; }

    public JobStatus? Status { get; private set; }

    public DateTimeOffset? StatusAt { get; private set; }

    public HoldReason? Hold { get; private set; }

    public DateTimeOffset? HoldAt { get; private set; }

    public string? Model { get; private set; }

    public string? Effort { get; private set; }

    public DateTimeOffset? ModelAt { get; private set; }

    public int Attempts { get; private set; }

    public VerificationOutcome? LastOutcome { get; private set; }

    public DateTimeOffset? OutcomeAt { get; private set; }

    public TokenTotals Tokens { get; private set; } = new();

    public IReadOnlyCollection<CostTotal> Costs => costs;

    public DateTimeOffset FirstActivityAt { get; private set; }

    public DateTimeOffset LastActivityAt { get; private set; }

    public HoldReason? VisibleHold => HoldAt >= StatusAt || StatusAt is null ? Hold : null;

    public static FleetJob Start(Guid instanceId, Guid jobId, EventTime at) => new(instanceId, jobId, at.At);

    public static string? RefusesSubmission(FleetJob? job, RepositoryKey repository) =>
        job?.Repository is { } known && known != repository ? "the job already belongs to another repository" : null;

    public void Submit(RepositoryKey repository, Guid? parentId, ModelChoice choice, EventTime at)
    {
        if (RefusesSubmission(this, repository) is { } refusal)
        {
            throw new InvalidOperationException(refusal);
        }

        Repository = repository;
        ParentId ??= parentId;

        if (choice.Model is not null || choice.Effort is not null)
        {
            ChooseModel(choice, at);
        }

        Touch(at);
    }

    public void Progress(JobStatus status, EventTime at)
    {
        if (StatusAt is null || at.At >= StatusAt)
        {
            Status = status;
            StatusAt = at.At;
        }

        Touch(at);
    }

    public void HoldFor(HoldReason reason, EventTime at)
    {
        if (HoldAt is null || at.At >= HoldAt)
        {
            Hold = reason;
            HoldAt = at.At;
        }

        Touch(at);
    }

    public void ChooseModel(ModelChoice choice, EventTime at)
    {
        if (ModelAt is null || at.At >= ModelAt)
        {
            Model = choice.Model;
            Effort = choice.Effort;
            ModelAt = at.At;
        }

        Touch(at);
    }

    public void Verify(Attempt attempt, EventTime at)
    {
        if (attempt.Number > Attempts || (attempt.Number == Attempts && (OutcomeAt is null || at.At >= OutcomeAt)))
        {
            Attempts = attempt.Number;
            LastOutcome = attempt.Outcome;
            OutcomeAt = at.At;
        }

        Touch(at);
    }

    public void Spend(TokenCounts tokens, CostAmount? cost, EventTime at)
    {
        Tokens.Add(tokens);

        if (cost is not null)
        {
            CostTotal.Add(costs, cost);
        }

        Touch(at);
    }

    private void Touch(EventTime at)
    {
        FirstActivityAt = at.At < FirstActivityAt ? at.At : FirstActivityAt;
        LastActivityAt = at.At > LastActivityAt ? at.At : LastActivityAt;
    }
}
