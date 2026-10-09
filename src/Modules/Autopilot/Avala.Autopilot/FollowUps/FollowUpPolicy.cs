using System.Text.Json;
using Avala.Agents.Contracts.Sessions;
using Avala.Autopilot.Approving;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Evidence;
using Avala.Autopilot.Sourcing;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.FollowUps;

internal sealed record ProposingSession(SessionId Session, Option<JobId> Job, Option<string> Worktree);

internal sealed class FollowUpPolicy(IPermissionAudit audit, IAutopilotRules rules, IJobCatalog catalog, TimeProvider clock)
{
    private const int MaximumInstruction = 4_000;

    public async Task<FollowUpDecision> DecideAsync(ProposingSession proposing, string input, CancellationToken cancellationToken)
    {
        var instruction = InstructionIn(input);
        var decided = new FollowUpDecision(proposing.Session, proposing.Job, instruction.Match(text => text, () => string.Empty), clock.GetUtcNow());

        if (instruction.IsNone)
        {
            return decided with { Refusal = FollowUpRefusal.MalformedInput };
        }

        if (proposing.Job.IsNone || proposing.Worktree.IsNone)
        {
            return decided with { Refusal = FollowUpRefusal.NoJob };
        }

        if (audit.AutonomyOf(proposing.Session).Match(applied => applied.Effective != Autonomy.Autonomous, () => true))
        {
            return decided with { Refusal = FollowUpRefusal.NotAutonomous };
        }

        var declared = await proposing.Worktree.Match(
            worktree => rules.OfWorktreeAsync(worktree, cancellationToken),
            () => Task.FromResult(Result<AutopilotRules, AutopilotError>.Failure(AutopilotError.Unreadable)));

        if (!declared.TryGetValue(out var repositoryRules, out _))
        {
            return decided with { Refusal = FollowUpRefusal.UnreadableRules };
        }

        if (repositoryRules.FollowUps != FollowUpRule.Accept)
        {
            return decided with { Refusal = FollowUpRefusal.NotAllowed };
        }

        var repository = await RepositoryOfAsync(proposing.Job, cancellationToken);

        return repository.Match(
            path => decided with { Task = new SourcedTask(FollowUpSource.Source, Guid.CreateVersion7().ToString("N"), RepositoryKey.Of(path), decided.Instruction) },
            () => decided with { Refusal = FollowUpRefusal.NoJob });
    }

    private async Task<Option<string>> RepositoryOfAsync(Option<JobId> job, CancellationToken cancellationToken) =>
        await job.Match(
            async known => (await catalog.HistoryAsync(known, cancellationToken)).Map(history => history.Summary.Repository),
            () => Task.FromResult(Option<string>.None));

    private static Option<string> InstructionIn(string input)
    {
        try
        {
            using var document = JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 2, AllowDuplicateProperties = false });
            var root = document.RootElement;

            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("instruction", out var given)
                && given.ValueKind == JsonValueKind.String
                && given.GetString() is { Length: > 0 and <= MaximumInstruction } text
                && !string.IsNullOrWhiteSpace(text)
                ? text
                : Option<string>.None;
        }
        catch (JsonException)
        {
            return Option<string>.None;
        }
    }
}
