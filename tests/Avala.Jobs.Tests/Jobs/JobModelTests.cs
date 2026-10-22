using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Jobs;
using Avala.Sdk;
using Avala.Sdk.Domain;
using AttemptOutcome = Avala.Jobs.Contracts.AttemptOutcome;
using HoldReason = Avala.Jobs.Contracts.HoldReason;
using JobFailure = Avala.Jobs.Contracts.JobFailure;

namespace Avala.Jobs.Tests.Jobs;

public sealed class JobModelTests
{
    private const int Sequences = 300;

    private const int StepsPerSequence = 200;

    private const int EndingOdds = 40;

    private const int RejectedOdds = 5;

    private const int Candidates = 8;

    private static readonly ConnectionName[] Connections = [Given.Connection, new("personal"), new("spare")];

    private static readonly Command[] Ending = [Command.Discard, Command.Fail];

    private static readonly Command[] Going = [.. Enum.GetValues<Command>().Except(Ending)];

    private static readonly JobState[] Ended = [JobState.Approved, JobState.Discarded, JobState.Failed];

    [Fact]
    public void GeneratedSequencesOfCommandsLeaveTheJobWhereTheReferenceModelSaysAndKeepItsInvariants()
    {
        var reached = new HashSet<JobState>();
        var accepted = new HashSet<Command>();

        for (var seed = 0; seed < Sequences; seed++)
        {
            var random = new Random(seed);
            var budget = 1 + random.Next(3);
            var job = Given.Job(budget);
            var model = new JobModel(job.Id, budget);
            var played = new List<Step>();

            for (var step = 0; step < StepsPerSequence; step++)
            {
                var next = Pick(random, model);
                played.Add(next);

                var actual = Apply(job, next);
                var expected = model.Apply(next);

                var disagreement = Disagreement(job, model, actual, expected);
                Assert.True(disagreement is null, $"Seed {seed}, {budget} attempts per round, after {string.Join(", ", played)}: {disagreement}");

                reached.Add(job.State);
                _ = expected.Error is null && accepted.Add(next.Command);
            }
        }

        Assert.Equal(Enum.GetValues<JobState>().Except([JobState.Open, JobState.Active]).Order(), reached.Order());
        Assert.Equal(Enum.GetValues<Command>().Order(), accepted.Order());
    }

    private static Step Pick(Random random, JobModel model)
    {
        var candidates = Enumerable.Range(0, Candidates).Select(_ => new Step(
            random.Next(EndingOdds) == 0 ? Ending[random.Next(Ending.Length)] : Going[random.Next(Going.Length)],
            Enum.GetValues<HoldReason>()[random.Next(Enum.GetValues<HoldReason>().Length)],
            Connections[random.Next(Connections.Length)])).ToList();

        return random.Next(RejectedOdds) == 0 ? candidates[0] : candidates.FirstOrDefault(model.Accepts) ?? candidates[0];
    }

    private static string? Disagreement(Job job, JobModel model, Expected actual, Expected expected) =>
        FirstOf(
        [
            (actual != expected, $"the job answered {Shown(actual)} where the model expects {Shown(expected)}"),
            (job.State != model.State, $"the job is {job.State} where the model expects {model.State}"),
            (job.Held != model.Held.ToOption(), $"the job's hold is {job.Held} where the model expects {model.Held}"),
            (job.Failure != model.Failure.ToOption(), $"the job's failure is {job.Failure} where the model expects {model.Failure}"),
            (job.Ended.IsSome != model.Ended, $"the job's end is {job.Ended} where the model expects ended {model.Ended}"),
            (job.Connection != model.Connection.ToOption(), $"the job's connection is {job.Connection} where the model expects {model.Connection}"),
            (!job.Attempts.Select(attempt => (attempt.Origin, attempt.Outcome)).SequenceEqual(model.Attempts), $"the job's attempts are {string.Join(", ", job.Attempts.Select(attempt => (attempt.Origin, attempt.Outcome)))} where the model expects {string.Join(", ", model.Attempts)}"),
            .. Broken(job),
        ]);

    private static IEnumerable<(bool Broken, string Why)> Broken(Job job)
    {
        var underway = job.Attempts.Where(attempt => attempt.IsUnderway).ToList();

        return
        [
            ((job.State == JobState.Failed) != job.Failure.IsSome, "a job fails exactly when it records why"),
            (job.Held.IsSome && job.State is not (JobState.NeedsHelp or JobState.Discarded), "a job's hold agrees with its status"),
            (job.Ended.IsSome != Ended.Contains(job.State), "a job has ended exactly when it is approved, discarded or failed"),
            (underway.Count > (job.State is JobState.Running or JobState.Checking ? 1 : 0) || (underway is [var only] && only != job.Attempts[^1]), "only the last attempt of a working job is underway"),
            (!job.Attempts.Select(attempt => attempt.Number.Value).SequenceEqual(Enumerable.Range(1, job.Attempts.Count)), "attempts are numbered from one without a gap"),
        ];
    }

    private static string? FirstOf(IEnumerable<(bool Broken, string Why)> checks) =>
        checks.Where(check => check.Broken).Select(check => check.Why).FirstOrDefault();

    private static Expected Apply(Job job, Step step) => step.Command switch
    {
        Command.Submit => Of(job.Submit()),
        Command.Start => Of(job.Start(Given.Workspace, Given.Session, Given.Connection)),
        Command.Recover => Of(job.Recover(Given.Session, resumed: true)),
        Command.Recheck => Of(job.Recheck()),
        Command.CompleteTurn => Of(job.CompleteTurn()),
        Command.Pass => Of(job.Pass()),
        Command.Retry => Of(job.Retry(Given.Feedback)),
        Command.RequestHelp => Of(job.RequestHelp()),
        Command.Hint => Of(job.Hint(Given.Feedback)),
        Command.HandOff => Of(job.HandOff(Given.Feedback, Given.Session, step.Connection)),
        Command.SendBack => Of(job.SendBack(Given.Feedback)),
        Command.Wake => Of(job.Wake(Given.Feedback)),
        Command.ContinueFromDevice => Of(job.ContinueFromDevice(Given.Feedback)),
        Command.Approve => Of(job.Approve(Given.Ended)),
        Command.Discard => Of(job.Discard(Given.Ended)),
        Command.Fail => Of(job.Fail(JobFailure.AgentFailed, Given.Ended)),
        Command.Hold => Of(job.Hold(step.Reason)),
        Command.CouldNotStart => Of(job.CouldNotStart(JobModel.Problem)),
        Command.RetryLaunch => Of(job.RetryLaunch()),
        Command.Reopen => Of(job.Reopen(Given.Feedback)),
        Command.ChooseModel => Of(job.ChooseModel(Given.Model)),
        _ => throw new ArgumentOutOfRangeException(nameof(step)),
    };

    private static Expected Of<TEvent>(Result<TEvent, JobError> result)
        where TEvent : IDomainEvent =>
        result.Match(happened => new Expected(null, happened), error => new Expected(error, null));

    private static string Shown(Expected answer) => answer.Error?.ToString() ?? answer.Event?.ToString() ?? "nothing";
}
