using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Host.Composition;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Runtime.Diagnostics;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Presentation;
using Avala.Sdk.Processes;
using Avala.Sdk.Regions;
using Avala.Shell;
using Avala.Shell.Regions;
using Avala.Testing.UI;
using Avala.Verification.Contracts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal sealed class DogfoodDriver(PublishedPlugins plugins, DogfoodJournal journal, string folder, string screens) : IDisposable
{
    private readonly DogfoodCamera camera = new(journal, screens);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Channel<IIntegrationEvent> events = Channel.CreateUnbounded<IIntegrationEvent>();
    private CancellationTokenSource subscriptions = new();
    private LogFile log = null!;
    private bool restarted;
    private DogfoodRestart? sinceRestart;
    private decimal spent = decimal.Parse(Environment.GetEnvironmentVariable("AVALA_DOGFOOD_SPENT_BEFORE") is { Length: > 0 } before ? before : "0", CultureInfo.InvariantCulture);
    private int turns;
    private JobId job;
    private string worktree = string.Empty;
    private CompositionRoot root = null!;
    private ShellViewModel shell = null!;
    private Window window = null!;

    private string Repository => DogfoodRepository.Repository(folder);

    public void Dispose() => subscriptions.Dispose();

    private string Data => DogfoodRepository.Data(folder);

    private string Report => Path.Combine(folder, "report");

    private Bound Toolbar => Region(ShellRegions.Toolbar).Single();

    private Bound Sidebar => Region(ShellRegions.Sidebar).Single();

    private Bound Jobs => Page("Jobs");

    public async Task RunAsync()
    {
        if (Resumed.Length == 0)
        {
            await DogfoodRepository.PrepareAsync(journal, folder);
        }

        await ComposeAsync();

        try
        {
            await DriveAsync();
        }
        catch (Exception failure)
        {
            await journal.NoteAsync($"DRIVER FAILURE: {failure}");
            await camera.ShootAsync("zz-driver-failure");
            throw;
        }
        finally
        {
            await journal.NoteAsync($"Total reported cost {spent.ToString(CultureInfo.InvariantCulture)} USD over {turns} turns, {clock.Elapsed:hh\\:mm\\:ss}");
            await StopAsync();
            await DogfoodLeftovers.LateEventsAsync(journal, Data);
        }
    }

    private async Task ComposeAsync()
    {
        var paths = new AvalaPaths(Data);
        log = new LogFile(paths, TimeProvider.System);
        root = CompositionRoot.Create(plugins.Directory, paths, log, Option<HttpMessageHandler>.None);
        subscriptions = new CancellationTokenSource();
        Subscribe();
        root.Start();
        shell = root.Services.GetRequiredService<ShellViewModel>();
        window = new ShellView { Width = 1440, Height = 900 };
        window.DataTemplates.Add(root.Views);
        window.DataContext = shell;
        Avala.Components.UI.Theme.Motion.SetIsReduced(window, true);
        shell.Activate();
        window.Show();
        camera.Window = window;
        await journal.NoteAsync($"Composed the real application from {plugins.Directory}, data folder {Data}, repository {Repository}");
    }

    private async Task StopAsync()
    {
        shell.Deactivate();
        window.Close();
        await subscriptions.CancelAsync();
        await root.DisposeAsync();
        await log.DisposeAsync();
        subscriptions.Dispose();
        await journal.NoteAsync("Stopped the application");
    }

    private async Task RestartAsync(DogfoodRound progress)
    {
        restarted = true;
        var before = await ConversationAsync("conversation-before-restart.txt");
        await InspectAsync($"r{progress.Number}-checking-before-restart");
        await journal.NoteAsync($"RESTART: stopping Avala while the job is Checking, after {turns} reported turns; the conversation shows {before.Count} entries");
        await StopAsync();

        while (events.Reader.TryRead(out _))
        {
        }

        var orphans = await DogfoodLeftovers.OrphansAsync();
        await journal.NoteAsync(orphans.Count == 0 ? $"After the stop no '{SlowCheck}' of the checks is left running" : $"FINDING: after the stop the checks' '{SlowCheck}' still runs as pid {string.Join(", ", orphans)}");
        sinceRestart = new DogfoodRestart();
        await ComposeAsync();
        await camera.ShootAsync($"r{progress.Number}-restarted-first-frame");
        await SelectAsync();
        await UntilAsync(Jobs.Presentation, () => Jobs["Conversation"]["Entries"].Items.Any(entry => entry.Kind == "RestartViewModel"), "the restart note in the recalled conversation");
        await camera.ShootAsync($"r{progress.Number}-restarted-conversation");
        var after = await ConversationAsync("conversation-after-restart.txt");
        var kept = before.Count <= after.Count && before.SequenceEqual(after.Take(before.Count), StringComparer.Ordinal);
        await journal.NoteAsync(kept
            ? $"CONVERSATION KEPT: all {before.Count} entries before the restart read the same after it, then [{string.Join(" ⏎ ", after.Skip(before.Count))}]"
            : $"FINDING: the conversation changed across the restart; first difference at entry {before.Zip(after).TakeWhile(pair => pair.First == pair.Second).Count()} of {before.Count} before, {after.Count} after");
        await InspectAsync($"r{progress.Number}-checking-after-restart");
    }

    private async Task<List<string>> ConversationAsync(string file)
    {
        await UntilAsync(Jobs.Presentation, () => Jobs.Has("Conversation"), "the conversation");
        List<string> entries = [.. Jobs["Conversation"]["Entries"].Items.Select(entry => $"{entry.Kind} | {DogfoodPolicy.Describe(entry.Target)}".ReplaceLineEndings(" ⏎ "))];
        await File.WriteAllLinesAsync(Path.Combine(Report, file), entries, Cancellation);

        return entries;
    }

    private async Task DriveAsync()
    {
        await camera.ShootAsync("first-run");
        var newJob = Page("New job");

        if (Resumed.Length > 0)
        {
            await ResumeAsync();
            return;
        }

        Invoke(Toolbar, "NewJobCommand");
        await newJob["Loading"].Value<Task>();
        newJob.Set("Repository", Repository);
        await newJob["Previewing"].Value<Task>();
        newJob.Set("Instruction", Rehearsal.Length > 0 ? $"[simulate: {Rehearsal}] {Instruction}" : Instruction);
        var offered = newJob["Connections"].Items.Select(item => item.Text).ToList();
        await journal.NoteAsync($"New job page offers connections [{string.Join(", ", offered)}], autonomies [{string.Join(", ", newJob["Autonomies"].Items.Select(item => item.Text))}]");
        Assert.Contains(Connection, offered);
        newJob.Set("Connection", Connection);
        await journal.NoteAsync($"New job connection options with their capacity: [{string.Join(" | ", newJob["Options"].Items.Select(option => $"{option["Name"].Text}: '{option["Reading"].Text}' near limit {option["IsNearLimit"].Text}"))}]");
        await ShootConnectionsAsync();
        await journal.NoteAsync($"Route: {newJob["Route"].Text} (attention {newJob["IsRouteAttention"].Text}); autonomy {newJob["Autonomy"].Text}: {newJob["AutonomyNote"].Text}");
        await camera.ShootAsync("new-job-filled");
        await newJob.ExecuteAsync("SubmitCommand");
        await journal.NoteAsync($"Submitted: '{newJob["Submitted"].Text}', error '{newJob["Error"].Text}'");
        Assert.Equal(string.Empty, newJob["Error"].Text);
        job = newJob["LastSubmitted"].Value<Option<JobId>>().Match(submitted => submitted, () => throw new InvalidOperationException("Nothing was submitted."));
        await camera.ShootAsync("new-job-submitted");

        await SelectAsync();
        await RoundsAsync(1);
    }

    private async Task ShootConnectionsAsync()
    {
        var picker = window.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault(box => box.ItemsSource is System.Collections.IEnumerable items && items.Cast<object>().Any(item => item.GetType().Name == "ConnectionOptionViewModel"));

        if (picker is null)
        {
            await journal.NoteAsync("FINDING: no combo box in the New job page lists the connection options");
            return;
        }

        camera.OverlayPopups();
        picker.IsDropDownOpen = true;
        await camera.ShootAsync("new-job-connections-open");
        picker.IsDropDownOpen = false;
    }

    private async Task ResumeAsync()
    {
        var listed = await root.Services.GetRequiredService<IJobCatalog>().ListAsync(Cancellation);
        var found = Assert.Single(listed);
        job = found.Job;
        await journal.NoteAsync($"RESTARTED Avala on the same data folder; the catalog lists the job as {found.Status}");
        await SelectAsync();
        await camera.ShootAsync("restarted");
        await RoundsAsync(1);
    }

    private async Task RoundsAsync(int first)
    {
        var round = first;

        while (true)
        {
            var status = await RoundAsync(round);
            await journal.NoteAsync($"Round {round} settled as {status}, spent so far {spent.ToString(CultureInfo.InvariantCulture)} USD");

            if (status != JobStatus.AwaitingReview)
            {
                await InspectAsync($"r{round}-held");
                return;
            }

            var jobs = Jobs;
            var review = await OpenReviewAsync(jobs, round);

            if (round == 1 && spent < SendBackBelow)
            {
                review.Set("Feedback", ChangeRequest);
                await camera.ShootAsync($"r{round}-review-feedback");
                await review.ExecuteAsync("SendBackCommand");
                await journal.NoteAsync($"Sent back with feedback; outcome '{review["Outcome"].Text}', refusal '{review["Refusal"].Text}'");
                await camera.ShootAsync($"r{round}-review-sent-back");
                Invoke(jobs, "CloseReviewCommand");
                round++;
                continue;
            }

            await review.ExecuteAsync("ApproveCommand");
            await UntilAsync(jobs.Presentation, () => review["Status"].Value<JobStatus>() == JobStatus.Approved || review["Refusal"].Text.Length > 0, "the approval");
            await journal.NoteAsync($"Approve: status {review["Status"].Text}, outcome '{review["Outcome"].Text}', refusal '{review["Refusal"].Text}'");
            await camera.ShootAsync($"r{round}-review-approved");
            Invoke(jobs, "CloseReviewCommand");
            await InspectAsync("approved");
            return;
        }
    }

    private async Task<JobStatus> RoundAsync(int round)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        guard.CancelAfter(Guard);
        var progress = new DogfoodRound(round);

        await foreach (var happened in events.Reader.ReadAllAsync(guard.Token))
        {
            if (await HandleAsync(happened, progress) is { } settled)
            {
                return settled;
            }
        }

        throw new TimeoutException($"Round {round} did not settle within {Guard}.");
    }

    private async Task<JobStatus?> HandleAsync(IIntegrationEvent happened, DogfoodRound progress)
    {
        sinceRestart?.Count(happened);

        switch (happened)
        {
            case JobProgressed progressed when progressed.Job == job:
                return await ProgressedAsync(progressed.Status, progress);
            case AttemptVerified verified when verified.Report.Job == job:
                await journal.NoteAsync($"VERIFIED attempt {verified.Report.Attempt}: {verified.Report.Outcome}, checks [{string.Join(", ", verified.Report.Checks.Select(check => $"{check.Name} {check.Status} in {check.Duration.TotalSeconds:F1}s"))}]");
                break;
            case AgentActivity { Event: ItemStarted { Kind: ItemKind.Message } } when !progress.Replied:
                progress.Replied = true;
                await camera.ShootAsync($"r{progress.Number}-first-reply");
                break;
            case AgentActivity { Event: TurnCompleted completed }:
                await camera.ShootAsync($"r{progress.Number}-turn-{completed.Turn.Value}-end");
                break;
            case AgentActivity { Event: UsageReported reported }:
                await SpentAsync(reported);
                break;
            case AgentActivity { Event: ItemStarted { Kind: ItemKind.Command } started }:
                await File.AppendAllTextAsync(Path.Combine(Report, "commands.log"), $"{DateTime.Now:HH:mm:ss} {started.Title.ReplaceLineEndings(" ⏎ ")}{Environment.NewLine}", Cancellation);
                break;
            case PermissionDecided { Decision: var decision } when decision.Job == Option<JobId>.Some(job):
                await DogfoodLeftovers.RecordPermissionAsync(journal, Report, decision);

                if (decision.Delivery == DecisionDelivery.LeftToHuman)
                {
                    await AnswerPermissionAsync(decision);
                }

                break;
            case FormDecided { Decision: { Delivery: DecisionDelivery.LeftToHuman } form } when form.Job == Option<JobId>.Some(job):
                await AnswerFormAsync(form);
                break;
        }

        return null;
    }

    private async Task<JobStatus?> ProgressedAsync(JobStatus status, DogfoodRound progress)
    {
        if (status == JobStatus.Running && !progress.Running)
        {
            progress.Running = true;
            await SelectAsync();
            await camera.ShootAsync($"r{progress.Number}-running");
        }
        else if (status == JobStatus.Checking)
        {
            progress.Running = false;
            await SelectAsync();
            await camera.ShootAsync($"r{progress.Number}-checking-{++progress.Verifications}");

            if (!restarted && Resumed.Length == 0)
            {
                await RestartAsync(progress);
            }
        }
        else if (Settled.Contains(status))
        {
            await SelectAsync();
            await camera.ShootAsync($"r{progress.Number}-settled-{status}");

            if (sinceRestart is { } since)
            {
                await journal.NoteAsync($"SINCE THE RESTART until {status}: {since}");
                sinceRestart = null;
            }

            return status;
        }

        return null;
    }

    private async Task SpentAsync(UsageReported reported)
    {
        turns++;
        spent += reported.Cost.Match(cost => cost.Amount, () => 0m);

        if (spent < StopAt)
        {
            return;
        }

        await journal.NoteAsync($"DRIVER STOP: the reported cost {spent.ToString(CultureInfo.InvariantCulture)} reached the driver's own stop at {StopAt.ToString(CultureInfo.InvariantCulture)}");
        var composer = Jobs["Conversation"]["Composer"];

        if (((System.Windows.Input.ICommand)composer["StopCommand"].Target).CanExecute(null))
        {
            await composer.ExecuteAsync("StopCommand");
        }
    }

    private async Task AnswerPermissionAsync(PolicyDecision decision)
    {
        var toolbar = Toolbar;
        var decisions = await OpenDecisionsAsync(toolbar, item => item["IsPermission"].Value<bool>() && item["Target"].Text == decision.Target, $"the permission for {decision.Target}");
        var item = decisions["Items"].Items.First(candidate => candidate["IsPermission"].Value<bool>() && candidate["Target"].Text == decision.Target);
        decisions.Set("Selected", item.Target);
        var (allow, reason) = DogfoodPolicy.Judge(decision.Kind, decision.Target, worktree);
        await journal.DecideAsync($"{decision.Kind} '{decision.Target}' asked as '{item["Title"].Text}' ({item["Asking"].Text}) -> {(allow ? "ALLOW" : "DENY")}: {reason}");
        var chained = DogfoodPolicy.Chained(decision.Target) ? "-chained" : string.Empty;
        await camera.ShootOnceAsync($"decision-{decision.Kind}{chained}", $"decisions-popover-{decision.Kind}{chained}");

        if (allow)
        {
            await decisions.ExecuteAsync("AnswerCommand");
        }
        else
        {
            Invoke(decisions, "WriteNoteCommand");
            decisions.Set("Note", reason);
            await decisions.ExecuteAsync("DenyCommand");
        }

        Invoke(toolbar, "CloseDecisionsCommand");
    }

    private async Task AnswerFormAsync(FormDecision form)
    {
        var toolbar = Toolbar;
        var decisions = await OpenDecisionsAsync(toolbar, item => !item["IsPermission"].Value<bool>(), "the form");
        var item = decisions["Items"].Items.First(candidate => !candidate["IsPermission"].Value<bool>());
        decisions.Set("Selected", item.Target);
        var answers = new List<string>();
        var fields = item["Card"]["Fields"].Items;

        foreach (var field in fields)
        {
            var choices = field["Choices"].Items;
            var chosen = choices.Where(choice => choice["Recommended"].Value<bool>()).Concat(choices).Cast<Bound?>().FirstOrDefault();

            if (field["IsConfirmation"].Value<bool>())
            {
                field.Set("Confirmed", true);
                answers.Add($"{field["Header"].Text}: confirmed");
            }
            else if (chosen is { } choice)
            {
                choice.Set("IsSelected", true);
                answers.Add($"{field["Header"].Text}: {choice["Label"].Text}");
            }
            else if (field["AcceptsText"].Value<bool>())
            {
                field.Set("Text", "Use your best judgment, state the assumption you make and go on.");
                answers.Add($"{field["Header"].Text}: best judgment");
            }
        }

        await journal.DecideAsync($"Form '{item["Title"].Text}' ({form.Form.Purpose}) with {fields.Count} fields -> {string.Join("; ", answers)}");
        await camera.ShootOnceAsync("decision-form", "decisions-popover-form");
        await decisions.ExecuteAsync("AnswerCommand");
        Invoke(toolbar, "CloseDecisionsCommand");
    }

    private async Task<Bound> OpenDecisionsAsync(Bound toolbar, Func<Bound, bool> wanted, string what)
    {
        var decisions = toolbar["Decisions"];

        if (!toolbar["IsDecisionsOpen"].Value<bool>())
        {
            camera.OverlayPopups();
            Invoke(toolbar, "ToggleDecisionsCommand");
        }

        await UntilAsync(decisions.Value<IPresentation>(), () => decisions["Items"].Items.Any(wanted), $"{what} in the decisions popover");

        return decisions;
    }

    private async Task<Bound> OpenReviewAsync(Bound jobs, int round)
    {
        await UntilAsync(jobs.Presentation, () => ((System.Windows.Input.ICommand)jobs["OpenReviewCommand"].Target).CanExecute(null), "the review sheet to open");
        await jobs.ExecuteAsync("OpenReviewCommand");
        var review = jobs["Review"];
        await UntilAsync(jobs.Presentation, () => review["IsLoaded"].Value<bool>(), "the review to load");
        var report = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"Review round {round}: {review["Heading"].Text} / {review["Title"].Text}")
            .AppendLine(CultureInfo.InvariantCulture, $"Facts: {review["Facts"].Text}")
            .AppendLine(CultureInfo.InvariantCulture, $"Verdict: {review["Verdict"].Text} (verified {review["IsVerified"].Text})")
            .AppendLine(CultureInfo.InvariantCulture, $"Proof: {review["Proof"].Text}")
            .AppendLine(CultureInfo.InvariantCulture, $"Quiet: {review["Quiet"].Text}")
            .AppendLine(CultureInfo.InvariantCulture, $"Changes: {review["Changes"].Text} — {review["Totals"].Text}");

        foreach (var exception in review["Exceptions"].Items)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"Exception: {DogfoodPolicy.Describe(exception.Target)}");
        }

        foreach (var file in review["Files"].Items)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"File: {file["Path"].Text} {file["Counts"].Text}");
        }

        await camera.ShootAsync($"r{round}-review-sheet");

        foreach (var file in review["Files"].Items.Where(file => file["Path"].Text.EndsWith(".js", StringComparison.Ordinal)).Take(2))
        {
            await file.ExecuteAsync("ShowHunksCommand");
            var lines = file["Hunks"].Items.SelectMany(hunk => hunk["Lines"].Items.Select(line => line["Text"].Text)).ToList();
            report.AppendLine(CultureInfo.InvariantCulture, $"Hunks of {file["Path"].Text}: {lines.Count} lines, first: {string.Join(" | ", lines.Take(6))}");
        }

        await camera.ShootAsync($"r{round}-review-sheet-hunks");
        await journal.NoteAsync(report.ToString());

        return review;
    }

    private async Task InspectAsync(string label)
    {
        var jobs = Jobs;

        if (!jobs["IsInspectorOpen"].Value<bool>())
        {
            Invoke(jobs, "ToggleInspectorCommand");
        }

        var sections = Region(ShellRegions.Inspector).ToList();

        foreach (var section in sections.Where(section => section.Target is IPresentation && section.Target.GetType().GetProperty("IsLoaded") is not null))
        {
            await UntilAsync(section.Value<IPresentation>(), () => section["IsLoaded"].Value<bool>(), $"{section.Kind} to load");
        }

        foreach (var section in sections)
        {
            await journal.NoteAsync($"Inspector {section.Kind}: {DogfoodPolicy.Describe(section.Target)}");
        }

        await camera.ShootAsync($"inspector-{label}");
    }

    private async Task SelectAsync()
    {
        var sidebar = Sidebar;
        var jobs = Jobs;

        await UntilAsync(sidebar.Value<IPresentation>(), () => Row(sidebar) is not null, "the job's row in the sidebar");
        Invoke(sidebar, "SelectCommand", Row(sidebar)!.Value.Target);
        await UntilAsync(jobs.Presentation, () => jobs.Has("Conversation"), "the conversation");

        if (worktree.Length == 0)
        {
            worktree = Directory.Exists(Path.Combine(Data, "worktrees")) ? Directory.GetDirectories(Path.Combine(Data, "worktrees")).FirstOrDefault() ?? string.Empty : string.Empty;
        }
    }

    private Bound? Row(Bound sidebar) =>
        Groups.SelectMany(group => sidebar[group].Items).Cast<Bound?>().FirstOrDefault(row => row!.Value["Job"].Value<JobId>() == job);

    private async Task UntilAsync(IPresentation presentation, Func<bool> shown, string what)
    {
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waited = Stopwatch.StartNew();

        void Check(object? sender, Presented presented)
        {
            if (Holds(shown))
            {
                seen.TrySetResult();
            }
        }

        presentation.Presented += Check;

        try
        {
            if (!Holds(shown))
            {
                await seen.Task.WaitAsync(TimeSpan.FromMinutes(2), Cancellation);
            }
        }
        catch (TimeoutException)
        {
            await journal.NoteAsync($"FINDING: waited 2 minutes for {what} and it never showed");
            await camera.ShootAsync($"zz-never-showed-{what.Replace(' ', '-')}");
            throw;
        }
        finally
        {
            presentation.Presented -= Check;
        }

        if (waited.Elapsed > TimeSpan.FromSeconds(5))
        {
            await journal.NoteAsync($"SLOW: {what} took {waited.Elapsed.TotalSeconds:F1}s to show");
        }
    }

    private static bool Holds(Func<bool> shown)
    {
        try
        {
            return shown();
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void Invoke(Bound target, string command, object? parameter = null) =>
        ((System.Windows.Input.ICommand)target[command].Target).Execute(parameter);

    private IEnumerable<Bound> Region(RegionName region) =>
        root.Services.GetServices<RegionContribution>().Where(contribution => contribution.Region == region).OrderBy(contribution => contribution.Order).Select(contribution => new Bound(contribution.ViewModel));

    private Bound Page(string title) => new(Assert.Single(root.Services.GetServices<IPage>(), page => page.Title == title));

    private void Subscribe()
    {
        var feed = root.Services.GetRequiredService<IEventFeed>();
        var types = DogfoodLeftovers.EventTypes();
        var listen = typeof(DogfoodDriver).GetMethod(nameof(ListenAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

        foreach (var type in types)
        {
            _ = (Task)listen.MakeGenericMethod(type).Invoke(this, [feed])!;
        }

        _ = journal.NoteAsync($"Listening to {types.Count} integration events: {string.Join(", ", types.Select(type => type.Name).Order(StringComparer.Ordinal))}");
    }

    private async Task ListenAsync<TEvent>(IEventFeed feed)
        where TEvent : IIntegrationEvent
    {
        try
        {
            await foreach (var happened in feed.SubscribeAsync<TEvent>(subscriptions.Token))
            {
                await journal.EventAsync(clock.Elapsed, happened);
                events.Writer.TryWrite(happened);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

}
