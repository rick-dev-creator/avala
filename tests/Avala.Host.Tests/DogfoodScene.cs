using System.Globalization;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Host.Tests;

public sealed class DogfoodStoppedException(string reason) : Exception(reason);

internal sealed record DogfoodStage(DogfoodScenario Scenario, DogfoodSetup Setup, DogfoodJournal Journal, CancellationToken Guard);

internal sealed class DogfoodScene(DogfoodStage stage, DogfoodApp app, DogfoodActions actions, DogfoodEvidence evidence)
{
    private readonly HashSet<JobId> stopped = [];
    private bool stopping;

    public DogfoodScenario Scenario => stage.Scenario;

    public DogfoodSetup Setup => stage.Setup;

    public DogfoodPlace Place => stage.Setup.Place;

    public DogfoodJournal Journal => stage.Journal;

    public DogfoodApp App => app;

    public DogfoodActions Actions => actions;

    public DogfoodEvidence Evidence => evidence;

    public bool Rehearsal => stage.Setup.Rehearsal;

    public int Mark => evidence.Count;

    public Func<PolicyDecision, Task<bool>>? Answering { get; set; }

    public string Tagged(string tag, string instruction) => stage.Setup.Tagged(tag, instruction);

    public async Task<JobId> SubmitAsync(string instruction, string connection, string label)
    {
        evidence.Note($"instruction:{label}", instruction);

        return await actions.SubmitAsync(Place.Repository, instruction, connection, label);
    }

    public async Task<T> UntilAsync<T>(Func<T, bool> match, string what, int since = 0)
        where T : IIntegrationEvent
    {
        var earlier = evidence.Seen.Skip(since).Select(seen => seen.Event).OfType<T>().Where(match).ToList();

        if (earlier.Count > 0)
        {
            return earlier[0];
        }

        try
        {
            await foreach (var happened in app.Events.Reader.ReadAllAsync(stage.Guard))
            {
                await HandleAsync(happened);

                if (happened is T found && match(found))
                {
                    return found;
                }
            }
        }
        catch (OperationCanceledException) when (stage.Guard.IsCancellationRequested && !DogfoodSettings.Cancellation.IsCancellationRequested)
        {
            throw new DogfoodStoppedException($"GUARD: {what} did not happen within {Scenario.Guard}");
        }

        throw new DogfoodStoppedException($"The event feed ended before {what}");
    }

    public Task<JobProgressed> StatusAsync(JobId job, string what, int since, params JobStatus[] statuses) =>
        UntilAsync<JobProgressed>(progressed => progressed.Job == job && statuses.Contains(progressed.Status), what, since);

    public async Task<JobStatus> SettledAsync(JobId job, string what, int since = 0) =>
        (await StatusAsync(job, what, since, DogfoodSettings.Settled)).Status;

    public async Task DrainAsync()
    {
        while (app.Events.Reader.TryRead(out var happened))
        {
            await HandleAsync(happened);
        }
    }

    public async Task RestartAsync()
    {
        await DrainAsync();
        await app.RestartAsync();
    }

    public async Task<string> ReadAsync(string key, string path)
    {
        var text = File.Exists(path) ? await File.ReadAllTextAsync(path, DogfoodSettings.Cancellation) : "(missing)";
        evidence.Note(key, text);

        return text;
    }

    public async Task<string> GitAsync(string key, string directory, params string[] arguments)
    {
        var text = Directory.Exists(directory) ? (await DogfoodRepository.RunAsync("git", ["-C", directory, .. arguments], new Dictionary<string, string?>())).Output : "(missing)";
        evidence.Note(key, text);

        return text;
    }

    public string WorktreeOf(JobId job) =>
        evidence.SessionsOf(job).Select(opened => opened.WorkingDirectory).FirstOrDefault() ?? string.Empty;

    public async Task StopAllAsync(string reason)
    {
        await Journal.NoteAsync($"DRIVER STOP: {reason}");

        foreach (var job in evidence.Jobs.Where(job => !stopped.Contains(job) && evidence.Journey(job) is [.., JobStatus.Running or JobStatus.Checking or JobStatus.Preparing]).ToList())
        {
            stopped.Add(job);
            await actions.StopAsync(job);
        }
    }

    private async Task HandleAsync(IIntegrationEvent happened)
    {
        evidence.Add(app.Clock.Elapsed, happened);

        switch (happened)
        {
            case JobProgressed progressed when progressed.Status is JobStatus.Running or JobStatus.Checking or JobStatus.NeedsHelp or JobStatus.AwaitingReview or JobStatus.Approved:
                await Journal.NoteAsync($"STATUS {evidence.Name(progressed.Job)}: {progressed.Status}");
                break;
            case AgentActivity { Event: UsageReported reported }:
                await SpentAsync(reported);
                break;
            case AgentActivity { Event: ItemStarted { Kind: ItemKind.Command or ItemKind.FileEdit } started }:
                await Journal.LogAsync("commands.log", $"{evidence.Name(evidence.JobOf(started.Session))} {started.Kind} {started.Title}");
                break;
            case AgentActivity { Event: ToolCalled called } when called.Tool.Contains("delegate", StringComparison.Ordinal) || called.Tool.Contains("child", StringComparison.Ordinal):
                await Journal.LogAsync("harness-tools.log", $"{evidence.Name(evidence.JobOf(called.Session))} called {called.Tool} {called.Input}");
                break;
            case AgentActivity { Event: ToolReturned returned }:
                await Journal.LogAsync("harness-tools.log", $"{evidence.Name(evidence.JobOf(returned.Session))} returned {returned.Item.Value} {returned.Result.Content}");
                break;
            case AgentActivity { Event: LimitReported limit }:
                await Journal.NoteAsync($"LIMIT on {evidence.ConnectionOf(limit.Session)}: {DogfoodJson.Show(limit.Limit)}");
                break;
            case PermissionDecided { Decision: var decision }:
                await DecidedAsync(decision);
                break;
            case FormDecided { Decision: { Delivery: DecisionDelivery.LeftToHuman } form }:
                await actions.AnswerFormAsync(form);
                break;
        }
    }

    private async Task DecidedAsync(PolicyDecision decision)
    {
        await DogfoodLeftovers.RecordPermissionAsync(Journal, decision, evidence);

        if (decision.Delivery != DecisionDelivery.LeftToHuman)
        {
            return;
        }

        if (decision.Parent.IsSome && decision.Passed.IsNone)
        {
            evidence.Find($"A request of the sub-agent {evidence.Name(decision.Job)} went to a person without a pass reason: {decision.Kind} {decision.Target}");
        }

        if (Answering is { } answering && await answering(decision))
        {
            return;
        }

        var worktree = decision.Job.Match(WorktreeOf, () => string.Empty);
        var (allow, reason) = DogfoodPolicy.Judge(decision.Kind, decision.Target, worktree);
        _ = await actions.AnswerPermissionAsync(decision, allow, reason, DogfoodRemember.Once);
    }

    private async Task SpentAsync(UsageReported reported)
    {
        var spent = evidence.Spent;
        await Journal.NoteAsync(string.Create(CultureInfo.InvariantCulture, $"SPENT {reported.Cost.Match(cost => $"{cost.Amount} {cost.Currency} ({cost.Basis})", () => "nothing")} on {evidence.ConnectionOf(reported.Session)}; scenario total {spent} USD of its cap {Scenario.Cap}"));

        if (spent < Scenario.Cap || stopping)
        {
            return;
        }

        stopping = true;
        evidence.Outcome = "stopped at the scenario cap";
        await StopAllAsync(string.Create(CultureInfo.InvariantCulture, $"the reported cost {spent} USD reached the scenario cap {Scenario.Cap} USD"));
        throw new DogfoodStoppedException("DRIVER STOP: the scenario cap was reached");
    }
}
