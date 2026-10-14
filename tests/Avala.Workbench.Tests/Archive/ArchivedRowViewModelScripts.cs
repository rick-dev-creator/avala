using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Workbench.Archive;
using Avala.Workbench.Archiving;
using Avala.Workbench.Board;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Archive;

public sealed class ArchivedRowViewModelScripts
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0.5, "Just now")]
    [InlineData(20, "20 min ago")]
    [InlineData(180, "3 h ago")]
    [InlineData(1500, "Yesterday")]
    [InlineData(4400, "3 d ago")]
    [InlineData(14400, "29 Sep")]
    public void ARowSaysWhenItsConversationEnded(double minutesAgo, string expected) =>
        Assert.Equal(expected, Row(JobStatus.Approved, TimeSpan.FromMinutes(minutesAgo), [], (_, _) => Task.CompletedTask).Ended);

    [Fact]
    public void ARowShowsItsTitleStatusAndDot()
    {
        var row = Row(JobStatus.AwaitingReview, TimeSpan.FromHours(1), [], (_, _) => Task.CompletedTask);

        Assert.Equal(("Update the dependency", "Ready for review", StatusKind.ReadyForReview), (row.Title, row.Status, row.Dot.Kind));
    }

    [Fact]
    public async Task RestoringAContestsRowRestoresEveryCandidateAsync()
    {
        var candidates = new[] { JobId.New(), JobId.New() };
        var restored = new List<JobId>();

        await Row(JobStatus.Approved, TimeSpan.FromHours(1), candidates, (jobs, _) =>
        {
            restored.AddRange(jobs);

            return Task.CompletedTask;
        }).RestoreCommand.ExecuteAsync(null);

        Assert.Equal(candidates, restored);
    }

    private static ArchivedRowViewModel Row(JobStatus status, TimeSpan endedAgo, JobId[] jobs, Func<IReadOnlyList<JobId>, CancellationToken, Task> restore)
    {
        var summary = new JobSummary(JobId.New(), "/repo", "Update the dependency", Now.AddDays(-30), status, default, default, default) { Ended = Now - endedAgo };
        var lead = new BoardJob(summary, Transcript.Empty);

        return new ArchivedRowViewModel(new ArchivedConversation(lead, summary.Instruction, "repo", Now - endedAgo, jobs.Length > 0 ? jobs : [lead.Job]), Now, _ => { }, restore);
    }
}
