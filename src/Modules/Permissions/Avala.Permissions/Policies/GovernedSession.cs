using System.Collections.Immutable;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Level = Avala.Jobs.Contracts.Autonomy;

namespace Avala.Permissions.Policies;

internal sealed record GovernedSession(
    SessionId Session,
    Option<string> WorkingDirectory,
    PermissionPolicy Policy,
    Option<SessionPolicy> Report,
    Option<JobId> Job,
    IReadOnlyList<PolicyDecision> Decisions)
{
    public GovernedSession(SessionId session)
        : this(session, Option<string>.None, PermissionPolicy.BuiltIn, Option<SessionPolicy>.None, Option<JobId>.None, [])
    {
    }

    public Option<SessionAutonomy> Autonomy { get; init; }

    public IReadOnlyList<FormDecision> Forms { get; init; } = [];

    public Option<JobId> AsksParent { get; init; }

    public ImmutableDictionary<ItemId, Option<PermissionRequest>> Escalated { get; init; } = ImmutableDictionary<ItemId, Option<PermissionRequest>>.Empty;

    public GovernedSession OpenedIn(string workingDirectory, PermissionPolicy policy, SessionPolicy report) =>
        this with { WorkingDirectory = workingDirectory, Policy = policy, Report = report };

    public GovernedSession WorkingOn(JobId job) => WorkingOn(job, Option<Level>.None, JobTerms.Full);

    public GovernedSession WorkingOn(JobId job, Option<Level> requested) => WorkingOn(job, requested, JobTerms.Full);

    public GovernedSession WorkingOn(JobId job, Option<Level> requested, JobTerms terms)
    {
        var policy = Policy.Capped(requested) with { ReadOnly = terms.ReadOnly };
        var refused = requested.Match(level => level > Policy.Declared, () => false);

        return this with
        {
            Job = job,
            Policy = policy,
            AsksParent = terms.AsksParent,
            Autonomy = new SessionAutonomy(Session, job, Policy.Declared, requested, policy.Autonomy, refused) { ReadOnly = terms.ReadOnly },
        };
    }

    public GovernedSession Decided(PolicyDecision decision) => this with { Decisions = [.. Decisions, decision] };

    public GovernedSession Asked(FormDecision decision) => this with { Forms = [.. Forms, decision] };

    public GovernedSession Escalating(ItemId item, Option<PermissionRequest> request) => this with { Escalated = Escalated.SetItem(item, request) };

    public GovernedSession Settled(ItemId item) => Escalated.ContainsKey(item) ? this with { Escalated = Escalated.Remove(item) } : this;

    public Option<PolicyDecision> WaitingPermission(ItemId item) =>
        Decisions.LastOrDefault(decision => decision.Item == item) is { Delivery: DecisionDelivery.LeftToHuman or DecisionDelivery.Undelivered or DecisionDelivery.LeftToParent } waiting
            ? waiting
            : Option<PolicyDecision>.None;

    public Option<FormDecision> WaitingForm(ItemId item) =>
        Forms.LastOrDefault(decision => decision.Item == item) is { Answer.IsNone: true, Delivery: not DecisionDelivery.Withdrawn } waiting ? waiting : Option<FormDecision>.None;

    public Option<PolicyDecision> WaitingParent(ItemId item) =>
        Escalated.ContainsKey(item) && Decisions.LastOrDefault(decision => decision.Item == item) is { Delivery: DecisionDelivery.LeftToParent } waiting
            ? waiting
            : Option<PolicyDecision>.None;

    public Option<FormDecision> FormWaitingParent(ItemId item) =>
        Escalated.ContainsKey(item) && Forms.LastOrDefault(decision => decision.Item == item) is { Delivery: DecisionDelivery.LeftToParent } waiting
            ? waiting
            : Option<FormDecision>.None;

    public GovernedSession Withdrawn(PolicyDecision decision) => Replaced(decision);

    public GovernedSession Withdrawn(FormDecision decision) => Replaced(decision);

    public GovernedSession Replaced(PolicyDecision decision) =>
        this with { Decisions = [.. Decisions.Select(kept => kept.Item == decision.Item ? decision : kept)] };

    public GovernedSession Replaced(FormDecision decision) =>
        this with { Forms = [.. Forms.Select(kept => kept.Item == decision.Item ? decision : kept)] };
}
