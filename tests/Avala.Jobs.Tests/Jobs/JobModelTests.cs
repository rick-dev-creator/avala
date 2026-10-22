using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Jobs;
using Avala.Sdk;
using Avala.Sdk.Domain;
using AttemptOutcome = Avala.Jobs.Contracts.AttemptOutcome;
using HoldReason = Avala.Jobs.Contracts.HoldReason;
using JobFailure = Avala.Jobs.Contracts.JobFailure;

namespace Avala.Jobs.Tests.Jobs;

public sealed class JobModelTests
{
    private const int Batches = 6;

    private const int SequencesPerBatch = 50;

    private const int StepsPerSequence = 200;

    private const int EndingOdds = 80;

    private const int RejectedOdds = 5;

    private static readonly ConnectionName[] Connections = [Given.Connection, new("personal"), new("spare")];

    private static readonly Command[] Ending = [Command.Discard, Command.Fail];

    private static readonly Command[] Going = [.. Enum.GetValues<Command>().Except(Ending)];

    private static readonly JobState[] Ended = [JobState.Approved, JobState.Discarded, JobState.Failed];

    [Fact]
    public void GeneratedSequencesOfCommandsLeaveTheJobWhereTheReferenceModelSaysAndKeepItsInvariants()
    {
        for (var batch = 0; batch < Batches; batch++)
        {
            var reached = new HashSet<JobState>();
            var accepted = new HashSet<Command>();

            for (var seed = batch * SequencesPerBatch; seed < (batch + 1) * SequencesPerBatch; seed++)
            {
                Play(seed, reached, accepted);
            }

            Assert.True(
                Enum.GetValues<JobState>().Except([JobState.Open, JobState.Active]).All(reached.Contains) && Enum.GetValues<Command>().All(accepted.Contains),
                $"Batch {batch} reached only {string.Join(", ", reached.Order())} and accepted only {string.Join(", ", accepted.Order())}");
        }
    }

    private static void Play(int seed, HashSet<JobState> reached, HashSet<Command> accepted)
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

    private static Step Pick(Random random, JobModel model)
    {
        var candidates = (random.Next(EndingOdds) == 0 ? Ending : Going).Select(command => new Step(
            command,
            Enum.GetValues<HoldReason>()[random.Next(Enum.GetValues<HoldReason>().Length)],
            Connections[random.Next(Connections.Length)],
            JobModel.Sessions[random.Next(JobModel.Sessions.Count)],
            random.Next(2) == 0,
            random.Next(2) == 0,
            random.Next(2) == 0)).ToList();
        var accepted = candidates.Where(model.Accepts).ToList();

        return random.Next(RejectedOdds) == 0 || accepted.Count == 0 ? candidates[random.Next(candidates.Count)] : accepted[random.Next(accepted.Count)];
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
            (job.Session != model.Session.ToOption(), $"the job's session is {job.Session} where the model expects {model.Session}"),
            (job.Resume != model.Resume.ToOption(), $"the job's resume token is {job.Resume} where the model expects {model.Resume}"),
            (job.Problem != model.Stuck.ToOption(), $"the job's problem is {job.Problem} where the model expects {model.Stuck}"),
            (job.Model != model.Model, $"the job's model is {job.Model} where the model expects {model.Model}"),
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
        Command.Start => Of(job.Start(Given.Workspace, step.Session, Given.Connection)),
        Command.Recover => Of(job.Recover(step.Session, step.Resumed)),
        Command.Recheck => Of(job.Recheck()),
        Command.CompleteTurn => Of(job.CompleteTurn()),
        Command.Pass => Of(job.Pass()),
        Command.Retry => Of(step.InSession ? job.Retry(Given.Feedback, step.Session, step.Resumed) : job.Retry(Given.Feedback)),
        Command.RequestHelp => Of(job.RequestHelp()),
        Command.Hint => Of(step.InSession ? job.Hint(Given.Feedback, step.Session, step.Resumed) : job.Hint(Given.Feedback)),
        Command.ContinueOn => Of(job.ContinueOn(Given.Feedback, step.Session, step.Connection)),
        Command.HandOff => Of(job.HandOff(Given.Feedback, step.Session, step.Connection)),
        Command.SendBack => Of(step.InSession ? job.SendBack(Given.Feedback, step.Session, step.Resumed) : job.SendBack(Given.Feedback)),
        Command.Wake => Of(job.Wake(Given.Feedback)),
        Command.ContinueFromDevice => Of(job.ContinueFromDevice(Given.Feedback)),
        Command.Approve => Of(job.Approve(Given.Ended)),
        Command.Discard => Of(job.Discard(Given.Ended)),
        Command.Fail => Of(job.Fail(JobFailure.AgentFailed, Given.Ended)),
        Command.Hold => Of(job.Hold(step.Reason)),
        Command.CouldNotStart => Of(job.CouldNotStart(JobModel.Problem)),
        Command.RetryLaunch => Of(job.RetryLaunch()),
        Command.Reopen => Of(Reopened(job, step)),
        Command.ChooseModel => Of(job.ChooseModel(JobModel.Chosen)),
        Command.RecordResume => Of(job.RecordResume(step.Session, JobModel.Token)),
        _ => throw new ArgumentOutOfRangeException(nameof(step)),
    };

    private static Result<AttemptStarted, JobError> Reopened(Job job, Step step)
    {
        var origin = step.Revisit ? Avala.Jobs.Contracts.AttemptOrigin.Hint : Avala.Jobs.Contracts.AttemptOrigin.Reopen;

        return step.InSession ? job.Reopen(Given.Feedback, step.Session, step.Resumed, origin) : job.Reopen(Given.Feedback, origin);
    }

    private static Expected Of<TEvent>(Result<TEvent, JobError> result)
        where TEvent : IDomainEvent =>
        result.Match(happened => new Expected(null, happened), error => new Expected(error, null));

    private static string Shown(Expected answer) => answer.Error?.ToString() ?? answer.Event?.ToString() ?? "nothing";
}
