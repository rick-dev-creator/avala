using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Notes.Taking;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Notes.Tests.Taking;

public sealed class NoteCommitsTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ANoteTakenWhileItsJobsTurnRunsIsCommittedWhenTheTurnCompletesAsync()
    {
        var job = JobId.New();
        var session = new SessionId(Guid.NewGuid());
        var turn = new TurnId(Guid.NewGuid());
        var recorded = new RecordedCommits();
        var commits = new NoteCommits(recorded);
        await commits.HandleAsync(new JobSessionStarted(job, session), Cancellation);
        await commits.HandleAsync(new AgentActivity(new TurnStarted(session, turn)), Cancellation);

        await commits.HandleAsync(new NoteTaken(Note(job)), Cancellation);
        var whileWorking = recorded.Paths.Count;
        await commits.HandleAsync(new AgentActivity(new TurnCompleted(session, turn, TurnOutcome.Finished)), Cancellation);

        Assert.Equal(0, whileWorking);
        Assert.Equal([".avala/notes/job/2026-10-19-1200-login-bug"], recorded.Paths);
    }

    private static NoteRecord Note(JobId job) =>
        new(NoteId.New(), NotePlace.OfJob(job, "/repo"), "Login bug", string.Empty, [], NoteState.Kept, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
        {
            Root = "/worktree",
            Folder = ".avala/notes/job/2026-10-19-1200-login-bug",
        };

    private sealed class RecordedCommits : IWorkingCommits
    {
        public List<string> Paths { get; } = [];

        public ValueTask<Result<WorkingCommit, WorkspaceFailure>> CommitAsync(string repository, string path, string message, CancellationToken cancellationToken)
        {
            Paths.Add(path);

            return ValueTask.FromResult<Result<WorkingCommit, WorkspaceFailure>>(new WorkingCommit("ba5e", "avala/job"));
        }
    }
}
