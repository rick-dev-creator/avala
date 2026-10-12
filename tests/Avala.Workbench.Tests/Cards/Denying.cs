using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Cards;

internal sealed class Denying
{
    public const string Style = "https://example.com/style";

    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    public SessionId Session { get; } = SessionId.New();

    public TurnId Turn { get; } = TurnId.New();

    public JobId Job { get; } = JobId.New();

    public static PolicyRule HostRule { get; } = new(RuleOrigin.Job, "always example.com in this job", ItemKind.Web, "example.com", RuleScope.Anywhere, PolicyAnswer.Allow);

    public PolicyDecision Decision(Autonomy autonomy = Autonomy.Autonomous, bool allowable = true) =>
        new(Session, Turn, new ItemId("fetch"), Job, ItemKind.Web, Style, PolicyAnswer.Deny, Option<PolicyRule>.None, DecisionDelivery.Answered, Now)
        {
            Autonomy = autonomy,
            Scope = new JobScope(allowable ? HostRule : Option<PolicyRule>.None, allowable ? Option<ScopeRefusal>.None : ScopeRefusal.StillAsked, Raisable: false),
        };

    public Transcript Transcript(PolicyDecision decision) =>
        Avala.Workbench.Timeline.Transcript.Empty
            .Apply(new TurnStarted(Session, Turn), Now)
            .Apply(new PermissionRequested(Session, Turn, new ItemId("fetch"), "Fetch https://example.com/style", ItemKind.Web, Style), Now)
            .Apply(decision);

    public DeniedEntry Entry(bool allowable = true) => Assert.Single(Transcript(Decision(allowable: allowable)).Entries.OfType<DeniedEntry>());
}
