using Avala.Jobs.Contracts;
using Avala.Testing.UI;
using Avala.Workbench.Review;
using Avala.Workbench.Reviewing;
using Avala.Workspaces.Contracts;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class ReviewViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheSheetShowsTheVerdictFirstThenTheExceptionsTheQuietLineAndTheDiffAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignReviewViewModel());

            Assert.Equal(("Verified on attempt 2 of 2", "3 files changed"), (view.TextOf("Verdict"), view.TextOf("Changes")));
            Assert.Equal(2, view.Find<ItemsControl>("Exceptions").ItemCount);
            Assert.Equal(3, view.Find<ItemsControl>("Files").ItemCount);
            Assert.True(view.HasClass("Verdict", "title"));
            Assert.False(view.Shows("Loading"));
            Assert.False(view.Find<Button>("SendBack").IsEffectivelyEnabled);
        }, Cancellation);

    [Fact]
    public Task WhileTheEvidenceLoadsTheSheetSaysSoAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var view = Screen.Show(new ReviewViewModel(JobId.New(), bench.Reader, bench.Desk, bench.Ui));

            Assert.Equal((true, false), (view.Shows("Loading"), view.Shows("Proof")));
        }, Cancellation);

    [Fact]
    public Task DiscardingTakesAClickAndAConfirmationAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            var review = new ReviewViewModel(bench.Job("Rate-limit POST /login", JobStatus.AwaitingReview).Job, bench.Reader, bench.Desk, bench.Ui);
            review.Track(JobStatus.AwaitingReview);
            var view = Screen.Show(review);

            view.Click("RequestDiscard");
            var asked = view.Shows("DiscardConfirmation");
            view.Click("ConfirmDiscard");
            await (review.ConfirmDiscardCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.True(asked);
            Assert.Equal((false, "Discarded"), (view.Shows("DiscardConfirmation"), view.TextOf("Outcome")));
            Assert.Equal(["discard"], bench.Jobs.Calls);
        }, Cancellation);

    [Fact]
    public Task TypedFeedbackSendsTheJobBackAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            var review = new ReviewViewModel(bench.Job("Rate-limit POST /login", JobStatus.AwaitingReview).Job, bench.Reader, bench.Desk, bench.Ui);
            review.Track(JobStatus.AwaitingReview);
            var view = Screen.Show(review);

            view.Type("Feedback", "Also limit by account");
            view.Click("SendBack");
            await (review.SendBackCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal(["send back Also limit by account"], bench.Jobs.Calls);
            Assert.Equal("Sent back for another round", view.TextOf("Outcome"));
        }, Cancellation);
}

public sealed class ReviewExceptionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AnExceptionShowsItsTitleAndTheTailOfItsOutputInTheCodeFontAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignReviewExceptionViewModel());

            Assert.Equal("Attempt 1 failed tests (exit 1)", view.TextOf("Title"));
            Assert.Equal("JetBrains Mono", view.Find<SelectableTextBlock>("Detail").FontFamily.FamilyNames[0]);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AnExceptionWithoutDetailShowsOnlyItsTitleAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new ReviewExceptionViewModel(new DeclinedForm(new Avala.Permissions.Contracts.FormDecision(
                Agents.Contracts.Sessions.SessionId.New(),
                Agents.Contracts.Sessions.TurnId.New(),
                new Agents.Contracts.Sessions.ItemId("q"),
                Sdk.Option<JobId>.None,
                new Agents.Contracts.Events.AgentForm(Agents.Contracts.Events.FormPurpose.Question, "Which route?", string.Empty, []),
                Autonomy.Supervised,
                Sdk.Option<Agents.Contracts.Sessions.FormAnswer>.None,
                [],
                Avala.Permissions.Contracts.DecisionDelivery.LeftToHuman,
                DateTimeOffset.UnixEpoch))));

            Assert.Equal(("Declined: Which route?", false), (view.TextOf("Title"), view.Shows("Detail")));
        }, TestContext.Current.CancellationToken);
}

public sealed class ChangedFileViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AnExpandedFileShowsItsHunksUnderItsPathAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignChangedFileViewModel());

            Assert.Equal(("src/auth/login.ts", "+24 -3", "Modified"), (view.TextOf("Path"), view.TextOf("Counts"), view.TextOf("Kind")));
            Assert.True(view.Shows("Hunks"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ClickingAFileShowsItsHunksAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            bench.Changes.Hunks["src/auth/login.ts"] = new FileDiff("src/auth/login.ts", false, [new DiffHunk(12, 6, 12, 14, "login", [new DiffLine(DiffLineKind.Added, "limiter.consume();")])]);
            var file = new ChangedFileViewModel(new FileChange("src/auth/login.ts", ChangeKind.Modified, 24, 3), new WorkspaceId(Guid.NewGuid()), bench.Reader);
            var view = Screen.Show(file);
            var collapsed = view.Shows("Hunks");

            view.Click("ShowHunks");
            await (file.ShowHunksCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal((false, true), (collapsed, view.Shows("Hunks")));
            Assert.Contains("+limiter.consume();", view.VisibleTexts);
        }, TestContext.Current.CancellationToken);
}

public sealed class HunkViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AHunkShowsItsHeaderAndEachMarkedLineInTheCodeFontAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignHunkViewModel());

            Assert.StartsWith("@@ -12,6 +12,14 @@", view.TextOf("Header"), StringComparison.Ordinal);
            Assert.Equal(6, view.Find<ItemsControl>("Lines").ItemCount);
            Assert.Contains("+  if (!attempt.allowed) {", view.VisibleTexts);
        }, TestContext.Current.CancellationToken);
}
