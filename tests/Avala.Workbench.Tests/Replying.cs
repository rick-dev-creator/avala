using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Replies;
using Avala.Workbench.Steering;

namespace Avala.Workbench.Tests;

internal static class Replying
{
    public static HumanReplies With(IPermissionAnswers permissions) => With(permissions, new FakeAgents());

    public static HumanReplies With(IPermissionAnswers permissions, IAgents agents) => With(permissions, agents, new FakeDeniedRequests(), new FakeJobs(), new JobBoard());

    public static HumanReplies With(IPermissionAnswers permissions, IAgents agents, IDeniedRequests denials, FakeJobs jobs, JobBoard board) =>
        new(permissions, agents, new DeniedRetries(denials, new JobSteering(jobs, new QueuedMessages(jobs, new Avala.Workbench.Following.Pulse(new Avala.Workbench.Board.JobBoard()))), board));
}

internal sealed class FakeDeniedRequests : IDeniedRequests
{
    public List<(SessionId Session, ItemId Item)> Allowed { get; } = [];

    public bool Refuse { get; set; }

    public ValueTask<Result<HumanAnswer, PolicyError>> AllowAsync(SessionId session, ItemId item, CancellationToken cancellationToken)
    {
        Allowed.Add((session, item));
        var rule = new PolicyRule(RuleOrigin.Job, "always example.com in this job", ItemKind.Web, "example.com", RuleScope.Anywhere, PolicyAnswer.Allow);

        return ValueTask.FromResult(Refuse
            ? Result<HumanAnswer, PolicyError>.Failure(PolicyError.NotAwaitingAnswer)
            : Result<HumanAnswer, PolicyError>.Success(new HumanAnswer(session, Job, item, ItemKind.Web, "https://example.com/style", PermissionAnswer.Allow, Option<string>.None, rule, DateTimeOffset.UnixEpoch)));
    }

    public JobId Job { get; } = JobId.New();
}
