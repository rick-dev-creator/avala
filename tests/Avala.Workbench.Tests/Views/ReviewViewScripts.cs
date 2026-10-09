using Avala.Autopilot.Contracts;
using Avala.Components.UI;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing.UI;
using Avala.Verification.Contracts;
using Avala.Workbench.Review;
using Avala.Workbench.Reviewing;
using Avala.Workspaces.Contracts;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Avala.Workbench.Tests.Views;

public sealed class ReviewViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheSheetLeadsWithTheDrawnVerdictThenWhatIsWorthALookTheQuietLineAndTheFoldedDiffAsync() =>
        ui.RunAsync(() =>
        {
            var view = Tall(Screen.Show(new DesignReviewViewModel()));

            Assert.Equal(("Ready for review", "Rate-limit POST /login", "ledger-api · claude-work · Autonomous · 2 sessions"), (view.TextOf("Heading"), view.TextOf("Title"), view.TextOf("Facts")));
            Assert.Equal(("Verified on attempt 2 of 2", "Worth a look · 4"), (view.TextOf("VerdictText"), view.TextOf("ExceptionCount")));
            Assert.Equal((true, true, false), (view.Shows("Check"), view.HasClass("Check", "draw"), view.Shows("Unproven")));
            Assert.Equal(4, view.Find<ItemsControl>("Exceptions").ItemCount);
            Assert.Equal(("5 files changed", "+130 −2", false), (view.Find<Fold>("Changes").Header, view.Find<Fold>("Changes").Fact, view.Shows("Files")));
            Assert.Equal((true, false, false, false), (view.Shows("Actions"), view.Shows("Stamp"), view.Shows("Loading"), view.Shows("FeedbackBox")));
            Assert.False(view.Find<Button>("SendBack").IsVisible);
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
    public Task AnUnverifiedJobIsNotDrawnWithTheCheckAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            var review = Review(bench);
            bench.Evidence.Runs[review.Job] = new RunEvidence(review.Job, new EvidenceSummary(1, Option<VerificationOutcome>.None, [], 0, 0, []), []);
            await review.Request(0)(Cancellation);
            var view = Tall(Screen.Show(review));

            Assert.Equal(("Not verified", "No checks ran in the worktree.", false, true), (view.TextOf("VerdictText"), view.TextOf("ProofText"), view.Shows("Check"), view.Shows("Unproven")));
        }, Cancellation);

    [Fact]
    public Task TheDiffOpensOnDemandAndAFileOpensItsHunksAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            bench.Changes.Files = [new FileChange("internal/http/routes.go", ChangeKind.Modified, 4, 1)];
            bench.Changes.Hunks["internal/http/routes.go"] = new FileDiff("internal/http/routes.go", false, [new DiffHunk(18, 7, 18, 10, "func Routes", [new DiffLine(DiffLineKind.Added, "r.With(ratelimit.PerIP(5))")])]);
            var review = Review(bench);
            await review.Request(0)(Cancellation);
            var view = Tall(Screen.Show(review));
            var folded = view.Shows("Files");

            view.Click(Header(view.Find<Fold>("Changes")));
            var opened = view.Shows("Files");
            view.Click("ShowHunks");
            await (review.Files[0].ShowHunksCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal((false, true, true), (folded, opened, view.Shows("Hunks")));
            Assert.Contains("+r.With(ratelimit.PerIP(5))", view.VisibleTexts);
        }, Cancellation);

    [Fact]
    public Task SendingBackRevealsTheFeedbackBoxFocusedAndSendsWhatIsTypedAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            var review = Review(bench);
            var view = Tall(Screen.Show(review));

            view.Click("WriteFeedback");
            var focused = view.Find<TextBox>("Feedback").IsFocused;
            view.Type("Feedback", "Also limit by account");
            view.Click("SendBack");
            await (review.SendBackCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.True(focused);
            Assert.Equal(["send back Also limit by account"], bench.Jobs.Calls);
            Assert.Equal(("Sent back", "Sent back with your feedback", true, false, false), (view.TextOf("Heading"), view.TextOf("Outcome"), view.Shows("Stamp"), view.Shows("Actions"), view.Shows("FeedbackBox")));
        }, Cancellation);

    [Fact]
    public Task ControlEnterInTheFeedbackSendsItBackAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            var review = Review(bench);
            var view = Tall(Screen.Show(review));
            view.Click("WriteFeedback");
            view.Type("Feedback", "Read the limits from config");

            view.Press(Key.Enter, RawInputModifiers.Control);
            await (review.SendBackCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.Equal(["send back Read the limits from config"], bench.Jobs.Calls);
        }, Cancellation);

    [Fact]
    public Task DiscardingAsksFirstAndKeepItGoesBackWithoutDiscardingAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            var review = Review(bench);
            var view = Tall(Screen.Show(review));

            view.Click("RequestDiscard");
            var asked = (view.Shows("DiscardConfirmation"), view.Shows("Actions"), view.Find<Button>("CancelDiscard").IsFocused);
            view.Click("CancelDiscard");
            var kept = (view.Shows("DiscardConfirmation"), view.Shows("Actions"), bench.Jobs.Calls.Count);
            view.Click("RequestDiscard");
            view.Click("ConfirmDiscard");
            await (review.ConfirmDiscardCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal((true, false, true), asked);
            Assert.Equal((false, true, 0), kept);
            Assert.Equal((false, "Discarded", "Discarded"), (view.Shows("DiscardConfirmation"), view.TextOf("Outcome"), view.TextOf("Heading")));
            Assert.Equal(["discard"], bench.Jobs.Calls);
        }, Cancellation);

    [Fact]
    public Task EscapeCancelsTheDiscardQuestionBeforeItClosesTheSheetAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var review = Review(bench);
            var closed = 0;
            review.Closed += (_, _) => closed++;
            var view = Tall(Screen.Show(review));
            view.Click("RequestDiscard");

            view.Press(Key.Escape);
            var afterFirst = (review.ConfirmingDiscard, closed);
            view.Press(Key.Escape);

            Assert.Equal((false, 0), afterFirst);
            Assert.Equal(1, closed);
            Assert.Empty(bench.Jobs.Calls);
        }, Cancellation);

    [Fact]
    public Task AConflictIsShownInPlaceAndTheActionsStayToTryAgainAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            bench.Jobs.Refusal = JobRejection.MergeConflict;
            bench.Changes.Conflicts = ["internal/http/routes.go", "config/defaults.yaml"];
            var review = Review(bench);
            var view = Tall(Screen.Show(review));

            view.Click("Approve");
            await (review.ApproveCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal("The work conflicts with the base branch in internal/http/routes.go, config/defaults.yaml.", view.TextOf("Refusal"));
            Assert.Equal((true, true, false), (view.Shows("Refused"), view.Shows("Actions"), view.Shows("Stamp")));
            Assert.Contains("config/defaults.yaml", view.VisibleTexts);
            Assert.True(view.Find<Button>("Approve").IsEffectivelyEnabled);
        }, Cancellation);

    [Fact]
    public Task ADirtyBaseCheckoutSaysWhatToDoNextInPlaceAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            bench.Jobs.Refusal = JobRejection.BaseCheckoutDirty;
            var review = Review(bench);
            var view = Tall(Screen.Show(review));

            view.Click("Approve");
            await (review.ApproveCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal("The base branch's checkout has uncommitted changes. Commit or stash them, then approve again.", view.TextOf("Refusal"));
            Assert.Equal((true, false), (view.Shows("Refused"), view.Shows("Conflicts")));
        }, Cancellation);

    [Fact]
    public Task ClickingApproveAgainWhileItRunsDeliversTheJobOnceAndStampsTheSheetAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bench.Jobs.Gate = gate;
            var review = Review(bench);
            var view = Tall(Screen.Show(review));

            view.Click("Approve");
            var running = review.ApproveCommand.ExecutionTask ?? Task.CompletedTask;
            view.Click("Approve");
            var during = (view.Find<Button>("Approve").IsEffectivelyEnabled, view.Find<Button>("RequestDiscard").IsEffectivelyEnabled);
            gate.SetResult();
            await running;
            view.Settle();

            Assert.Equal((false, false), during);
            Assert.Equal(["approve"], bench.Jobs.Calls);
            Assert.Equal(("Approved", "Approved: the branch avala/fix-the-test is ready", true), (view.TextOf("Heading"), view.TextOf("Outcome"), view.Find<Button>("BackToConversations").IsFocused));
        }, Cancellation);

    [Fact]
    public Task AReloadArrivingWhileApprovingKeepsTheProofOnScreenAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bench.Jobs.Gate = gate;
            var review = Review(bench);
            await review.Request(0)(Cancellation);
            var view = Tall(Screen.Show(review));

            view.Click("Approve");
            var running = review.ApproveCommand.ExecutionTask ?? Task.CompletedTask;
            bench.Changes.Files = [new FileChange("config/defaults.yaml", ChangeKind.Modified, 6, 0)];
            await review.Request(1)(Cancellation);
            view.Settle();
            var reloaded = (view.Shows("Proof"), view.Find<Fold>("Changes").Header);
            gate.SetResult();
            await running;
            view.Settle();

            Assert.Equal((true, (object?)"1 file changed"), reloaded);
            Assert.Equal((true, true), (view.Shows("Proof"), view.Shows("Stamp")));
        }, Cancellation);

    [Fact]
    public Task BackToConversationsAndTheCloseButtonCloseTheSheetAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            var review = Review(bench);
            var closed = 0;
            review.Closed += (_, _) => closed++;
            var view = Tall(Screen.Show(review));

            view.Click("Close");
            view.Click("Approve");
            await (review.ApproveCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();
            view.Click("BackToConversations");

            Assert.Equal(2, closed);
        }, Cancellation);

    [Fact]
    public Task ALongOutputTailWrapsInsideTheSheetAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            var job = bench.Job("Rate-limit POST /login", JobStatus.AwaitingReview);
            var tail = string.Join("\n", Enumerable.Range(1, 12).Select(line => $"ratelimit_test.go:{line}: attempt {line}: want 429, got 200 after {new string('x', 160)}"));
            bench.Evidence.Runs[job.Job] = new RunEvidence(job.Job, new EvidenceSummary(2, VerificationOutcome.Passed, [], 0, 0, []), [])
            {
                Verifications =
                [
                    new VerificationReport(job.Job, 1, VerificationOutcome.Failed, Option<FileOrigin>.None, [new CheckEvidence("test", "go test ./...", CheckStatus.Failed, 1, TimeSpan.Zero, tail, string.Empty)], GateVerdict.Pass, DateTimeOffset.UnixEpoch),
                ],
            };
            var review = new ReviewViewModel(job.Job, bench.Reader, bench.Desk, bench.Ui);
            review.Track(JobStatus.AwaitingReview);
            await review.Request(0)(Cancellation);
            var view = Tall(Screen.Show(review));

            var output = view.Find<SelectableTextBlock>("Output");
            Assert.True(view.Shows("Output"));
            Assert.True(output.Bounds.Width <= view.Find<Border>("Sheet").Bounds.Width);
            Assert.True(output.Bounds.Height > 12 * 18);
        }, Cancellation);

    private static ReviewViewModel Review(Bench bench)
    {
        var review = new ReviewViewModel(bench.Job("Rate-limit POST /login", JobStatus.AwaitingReview).Job, bench.Reader, bench.Desk, bench.Ui);
        review.Track(JobStatus.AwaitingReview);

        return review;
    }

    private static ViewScript Tall(ViewScript view)
    {
        view.Window.Width = 900;
        view.Window.Height = 1400;

        return view.Settle();
    }

    private static ToggleButton Header(Fold fold) => fold.GetVisualDescendants().OfType<ToggleButton>().First();
}

public sealed class ReviewExceptionViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AnOpenExceptionShowsItsFactAndTheTailOfItsOutputInTheCodeFontAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignReviewExceptionViewModel());

            Assert.Equal(("Attempt 1 failed", "test · exit 1"), (view.TextOf("Title"), view.Find<Fold>("Row").Fact));
            Assert.True(view.Shows("Output"));
            Assert.Equal("JetBrains Mono", view.Find<SelectableTextBlock>("Output").FontFamily.FamilyNames[0]);
        }, Cancellation);

    [Fact]
    public Task ClickingAFoldedExceptionOpensItAndClickingAgainFoldsItAsync() =>
        ui.RunAsync(() =>
        {
            var exception = new ReviewExceptionViewModel(new EditedRuleFile(".avala/checks.json"));
            var view = Screen.Show(exception);
            var folded = view.Shows("Detail");

            view.Click(view.Find<Fold>("Row").GetVisualDescendants().OfType<ToggleButton>().First());
            var opened = (view.Shows("Detail"), exception.IsExpanded);
            view.Click(view.Find<Fold>("Row").GetVisualDescendants().OfType<ToggleButton>().First());

            Assert.False(folded);
            Assert.Equal((true, true), opened);
            Assert.Equal((false, false), (view.Shows("Detail"), exception.IsExpanded));
        }, Cancellation);

    [Theory]
    [InlineData("Failure", true, false)]
    [InlineData("Attention", false, true)]
    [InlineData("Neutral", false, false)]
    public Task TheDotTellsHowMuchTheExceptionMattersAsync(string tone, bool failure, bool attention) =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignReviewExceptionViewModel { Tone = Enum.Parse<ExceptionTone>(tone) });

            Assert.Equal((failure, attention), (view.HasClass("Dot", "failure"), view.HasClass("Dot", "attention")));
        }, Cancellation);

    [Fact]
    public Task AnExceptionWithoutDetailShowsOnlyItsTitleWhenOpenedAsync() =>
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
                DateTimeOffset.UnixEpoch)))
            { IsExpanded = true });

            Assert.Equal(("Declined: Which route?", false, false), (view.TextOf("Title"), view.Shows("Detail"), view.Shows("OutputWell")));
        }, Cancellation);
}

public sealed class ChangedFileViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AnExpandedFileShowsItsHunksUnderItsPathAndNamesOnlyAnUnusualKindAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignChangedFileViewModel());

            Assert.Equal(("internal/http/routes.go", "+4 −1", false), (view.TextOf("Path"), view.TextOf("Counts"), view.Shows("Kind")));
            Assert.True(view.Shows("Hunks"));
            Assert.Equal("Added", Screen.Show(new DesignChangedFileViewModel("internal/http/ratelimit.go", ChangeKind.Added, "+71", false)).TextOf("Kind"));
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
    public Task AHunkShowsItsHeaderAndMarksAddedAndRemovedLinesInTheCodeFontAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignHunkViewModel());
            var lines = view.Find<ItemsControl>("Lines").GetVisualDescendants().OfType<TextBlock>().ToList();

            Assert.StartsWith("@@ -18,7 +18,10 @@", view.TextOf("Header"), StringComparison.Ordinal);
            Assert.Equal(5, lines.Count);
            Assert.Equal([false, true, false, false, false], lines.Select(line => line.Classes.Contains("removed")));
            Assert.Equal([false, false, true, true, false], lines.Select(line => line.Classes.Contains("added")));
            Assert.All(lines, line => Assert.Equal("JetBrains Mono", line.FontFamily.FamilyNames[0]));
        }, TestContext.Current.CancellationToken);
}
