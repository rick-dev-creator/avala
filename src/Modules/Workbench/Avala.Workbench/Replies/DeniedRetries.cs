using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Replies;

internal sealed record Retry(HumanAnswer Allowed, Option<JobRejection> Untold);

internal sealed class DeniedRetries(IDeniedRequests denials, JobSteering steering, JobBoard board)
{
    public async Task<Result<Retry, PolicyError>> AllowAsync(DeniedEntry denied, CancellationToken cancellationToken)
    {
        var allowed = await denials.AllowAsync(denied.Decision.Session, denied.Decision.Item, cancellationToken);

        return allowed.TryGetValue(out var answer, out var error)
            ? await TellAsync(answer, cancellationToken)
            : error;
    }

    public static string Told(HumanAnswer answer) =>
        $"Avala now allows {What(answer)} in this job. You may retry it.";

    private async Task<Result<Retry, PolicyError>> TellAsync(HumanAnswer answer, CancellationToken cancellationToken) =>
        await answer.Job.Match(
            async job =>
            {
                var shown = board.Find(job).Match(found => found.Status, () => JobStatus.Running);
                var told = await steering.SendAsync(job, shown, Told(answer), cancellationToken);

                return Result<Retry, PolicyError>.Success(new Retry(answer, told.Match(_ => Option<JobRejection>.None, Option<JobRejection>.Some)));
            },
            () => Task.FromResult(Result<Retry, PolicyError>.Success(new Retry(answer, Option<JobRejection>.None))));

    private static string What(HumanAnswer answer)
    {
        var subject = answer.Rule.Bind(rule => rule.Target).Match(target => target, () => answer.Target);

        return answer.Kind switch
        {
            ItemKind.Command => $"running `{subject}`",
            ItemKind.Web => $"web requests to {subject}",
            ItemKind.FileEdit => $"editing {subject}",
            ItemKind.Mcp => $"the tool {subject}",
            _ => subject,
        };
    }
}
