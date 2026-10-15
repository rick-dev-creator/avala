using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Tests.Coordination;
using Avala.Testing;

namespace Avala.Jobs.Tests.Holding;

public sealed class AttachmentTests
{
    private static readonly Attachment Screenshot = new("screen.png", "image/png", "/data/attachments/1/screen.png");

    private static readonly Attachment Log = new("build.log", "text/plain", "/data/attachments/2/build.log");

    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Fact]
    public async Task AJobStartedWithAttachmentsSendsThemWithItsInstructionAndKeepsThemOnItsFirstAttemptAsync()
    {
        var flow = JobFlow.With();

        var job = await flow.RunningAsync(JobFlow.Request() with { Attachments = [Screenshot] });

        Assert.Equal("Add GitHub login", flow.Agents.Said.Single().Text);
        Assert.Equal([Screenshot], flow.Agents.Said.Single().Attachments);
        Assert.Equal([Screenshot], Assert.Single(Outcomes.Present(await flow.Catalog.HistoryAsync(job.Id, Cancellation)).Attempts).Attachments);
    }

    [Fact]
    public async Task AMessageWithAttachmentsSendsAJobBackWithThemAndAVerificationRetryAfterCarriesNoneAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();
        await flow.FinishTurnAsync(job);

        Outcomes.Succeeds(await flow.Jobs.SendBackAsync(job.Id, new JobMessage("Use this layout") { Attachments = [Screenshot, Log] }, Cancellation));
        Outcomes.Succeeds(job.CompleteTurn());
        Outcomes.Succeeds(job.Retry(Outcomes.Succeeds(Feedback.Create("Two tests fail"))));

        Assert.Equal("Use this layout", flow.Agents.Said.Last().Text);
        Assert.Equal([Screenshot, Log], flow.Agents.Said.Last().Attachments);
        Assert.Equal([[], [Screenshot, Log], []], job.Attempts.Select(attempt => attempt.Attachments.ToArray()));
    }

    [Fact]
    public async Task AMessageWithAttachmentsSentWhileTheAgentWorksJoinsTheTurnWithThemAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();

        Outcomes.Succeeds(await flow.Jobs.SteerAsync(job.Id, new JobMessage("Like this") { Attachments = [Screenshot] }, Cancellation));

        var steered = flow.Agents.Said.Last();
        Assert.True(steered.MidTurn);
        Assert.Equal([Screenshot], steered.Attachments);
    }
}
