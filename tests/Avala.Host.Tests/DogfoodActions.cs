using System.Globalization;
using System.Text;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Shell;
using Avala.Sdk.Regions;
using Avalonia.Controls;
using Avalonia.VisualTree;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal sealed class DogfoodActions(DogfoodApp app, DogfoodJournal journal, DogfoodCamera camera, DogfoodEvidence evidence)
{
    private DogfoodUi Ui => app.Ui;

    private Bound Jobs => Ui.Jobs;

    public async Task<JobId> SubmitAsync(string repository, string instruction, string connection, string label, int candidates = 1)
    {
        ShowJobs();
        DogfoodUi.Invoke(Ui.Toolbar, "NewJobCommand");
        var newJob = Ui.Page("New job");
        await newJob["Loading"].Value<Task>();
        newJob.Set("Repository", repository);
        await newJob["Previewing"].Value<Task>();
        newJob.Set("Instruction", instruction);
        var offered = newJob["Connections"].Items.Select(item => item.Text).ToList();
        Assert.Contains(connection, offered);
        newJob.Set("Connection", connection);
        await newJob["Offering"].Value<Task>();
        var options = string.Join(" | ", newJob["Options"].Items.Select(option => $"{option["Name"].Text}: '{option["Reading"].Text}' near limit {option["IsNearLimit"].Text}"));
        evidence.Note($"new-job:{label}", $"connections [{string.Join(", ", offered)}]; options [{options}]; route {newJob["Route"].Text}; autonomy {newJob["Autonomy"].Text}: {newJob["AutonomyNote"].Text}");
        await CandidatesAsync(newJob, candidates, label);
        await ShootConnectionsAsync(label);
        await camera.ShootAsync($"{label}-new-job-filled");
        await newJob.ExecuteAsync("SubmitCommand");
        await journal.NoteAsync($"Submitted {label}: '{newJob["Submitted"].Text}', error '{newJob["Error"].Text}'");
        Assert.Equal(string.Empty, newJob["Error"].Text);
        var job = newJob["LastSubmitted"].Value<Option<JobId>>().Match(submitted => submitted, () => throw new InvalidOperationException("Nothing was submitted."));
        evidence.Note($"submitted:{label}", job.ToString());
        evidence.Note($"submitted-text:{label}", newJob["Submitted"].Text);

        return job;
    }

    public void ShowJobs()
    {
        var jobs = Ui.Page("Jobs");

        if (!ReferenceEquals(app.Shell.SelectedPage, jobs.Target))
        {
            app.Shell.SelectedPage = (IPage)jobs.Target;
        }

    }

    public async Task<Bound> SelectAsync(JobId job)
    {
        ShowJobs();
        var sidebar = Ui.Sidebar;
        var jobs = Jobs;
        await Ui.UntilAsync(sidebar.Value<IPresentation>(), () => Row(sidebar, job) is not null, $"the row of {evidence.Name(job)} in the sidebar");
        DogfoodUi.Invoke(sidebar, "SelectCommand", Row(sidebar, job)!.Value.Target);
        await Ui.UntilAsync(jobs.Presentation, () => jobs.Has("Conversation") && jobs["Conversation"]["Job"].Value<JobId>() == job, $"the conversation of {evidence.Name(job)}");

        return jobs["Conversation"];
    }

    public async Task<(string Group, string Fact)> RowAsync(JobId job, string key, string expected, string reading = "")
    {
        ShowJobs();
        var sidebar = Ui.Sidebar;
        string? Group() => Groups.FirstOrDefault(name => sidebar[name].Items.SelectMany(Rows).Any(row => row["Job"].Value<JobId>() == job));
        await Ui.UntilAsync(sidebar.Value<IPresentation>(), () => Group() == expected && Row(sidebar, job)!.Value["Fact"].Text.Contains(reading, StringComparison.Ordinal), $"the row of {evidence.Name(job)} in the sidebar group {expected} reading '{reading}'");
        var group = Group()!;
        var fact = Row(sidebar, job)!.Value["Fact"].Text;
        evidence.Note(key, $"{group}: {fact}");

        return (group, fact);
    }

    public async Task<string> PillAsync(JobId job, string key)
    {
        var conversation = await SelectAsync(job);
        var pill = conversation["Pill"]["Text"].Text;
        evidence.Note(key, pill);
        await camera.ShootAsync(key.Replace(':', '-'));

        return pill;
    }

    public async Task<IReadOnlyList<string>> ConversationAsync(JobId job, string key)
    {
        var conversation = await SelectAsync(job);
        List<string> entries = [.. conversation["Entries"].Items.Select(entry => $"{entry.Kind} | {DogfoodPolicy.Describe(entry.Target)}".ReplaceLineEndings(" ⏎ "))];
        evidence.Note(key, string.Join(Environment.NewLine, entries));
        await File.WriteAllLinesAsync(Path.Combine(journal.Folder, $"{key.Replace(':', '-')}.txt"), entries, Cancellation);

        return entries;
    }

    public async Task<IReadOnlyList<Bound>> InspectAsync(JobId job, string label)
    {
        await SelectAsync(job);
        var jobs = Jobs;

        if (!jobs["IsInspectorOpen"].Value<bool>())
        {
            DogfoodUi.Invoke(jobs, "ToggleInspectorCommand");
        }

        var sections = Ui.Region(ShellRegions.Inspector).ToList();

        foreach (var section in sections.Where(section => section.Target is IPresentation && section.Target.GetType().GetProperty("IsLoaded") is not null))
        {
            await Ui.UntilAsync(section.Value<IPresentation>(), () => section["IsLoaded"].Value<bool>(), $"{section.Kind} to load");
        }

        foreach (var section in sections)
        {
            evidence.Note($"inspector:{label}:{section.Kind}", DogfoodPolicy.Describe(section.Target));
        }

        await camera.ShootAsync($"inspector-{label}");

        return sections;
    }

    public async Task<Bound> SectionAsync(JobId job, string kind, string label) =>
        (await InspectAsync(job, label)).Single(section => section.Kind == kind);

    public async Task<Bound> OpenReviewAsync(JobId job, string label)
    {
        await SelectAsync(job);
        var jobs = Jobs;
        await Ui.UntilAsync(jobs.Presentation, () => DogfoodUi.CanInvoke(jobs, "OpenReviewCommand"), "the review sheet to open");
        await jobs.ExecuteAsync("OpenReviewCommand");
        var review = jobs["Review"];
        await Ui.UntilAsync(jobs.Presentation, () => review["IsLoaded"].Value<bool>(), "the review to load");
        var report = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"Heading: {review["Heading"].Text} / {review["Title"].Text}")
            .AppendLine(CultureInfo.InvariantCulture, $"Facts: {review["Facts"].Text}")
            .AppendLine(CultureInfo.InvariantCulture, $"Verdict: {review["Verdict"].Text} (verified {review["IsVerified"].Text})")
            .AppendLine(CultureInfo.InvariantCulture, $"Proof: {review["Proof"].Text}")
            .AppendLine(CultureInfo.InvariantCulture, $"Changes: {review["Changes"].Text} — {review["Totals"].Text}")
            .AppendLine(CultureInfo.InvariantCulture, $"Advice: {review["Advice"].Text} (resubmit offered {review["OffersResubmit"].Text})");

        foreach (var exception in review["Exceptions"].Items)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"Exception: {DogfoodPolicy.Describe(exception.Target)}");
        }

        foreach (var file in review["Files"].Items)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"File: {file["Path"].Text} {file["Counts"].Text}");
        }

        evidence.Note($"review:{label}", report.ToString());
        evidence.Note($"review:{label}:verified", review["IsVerified"].Text);
        evidence.Note($"review:{label}:files", string.Join(Environment.NewLine, review["Files"].Items.Select(file => $"{file["Path"].Text} {file["Counts"].Text}")));
        evidence.Note($"review:{label}:advice", review["Advice"].Text);
        evidence.Note($"review:{label}:resubmit", review["OffersResubmit"].Text);
        await camera.ShootAsync($"{label}-review-sheet");

        return review;
    }

    public async Task ApproveAsync(JobId job, string label)
    {
        var review = await OpenReviewAsync(job, label);
        var jobs = Jobs;
        await review.ExecuteAsync("ApproveCommand");
        await Ui.UntilAsync(jobs.Presentation, () => review["Status"].Value<JobStatus>() == JobStatus.Approved || review["Refusal"].Text.Length > 0, "the approval");
        evidence.Note($"approve:{label}", $"status {review["Status"].Text}, outcome '{review["Outcome"].Text}', refusal '{review["Refusal"].Text}'");
        await camera.ShootAsync($"{label}-approved");
        DogfoodUi.Invoke(jobs, "CloseReviewCommand");
    }

    public async Task<JobId> ResubmitAsync(JobId job, string label)
    {
        var review = await OpenReviewAsync(job, label);
        var jobs = Jobs;
        await Ui.UntilAsync(jobs.Presentation, () => DogfoodUi.CanInvoke(review, "ResubmitCommand"), "Resubmit to be offered");
        await review.ExecuteAsync("ResubmitCommand");
        await Ui.UntilAsync(jobs.Presentation, () => jobs.Has("Conversation") && jobs["Conversation"]["Job"].Value<JobId>() != job, "the resubmitted job's conversation");
        var resubmitted = jobs["Conversation"]["Job"].Value<JobId>();
        evidence.Note($"resubmit:{label}", $"outcome '{review["Outcome"].Text}', refusal '{review["Refusal"].Text}', workbench shows {resubmitted}");
        await camera.ShootAsync($"{label}-resubmitted");

        return resubmitted;
    }

    public async Task DiscardAsync(JobId job, string label)
    {
        var review = await OpenReviewAsync(job, label);
        DogfoodUi.Invoke(review, "RequestDiscardCommand");
        await review.ExecuteAsync("ConfirmDiscardCommand");
        evidence.Note($"discard:{label}", $"outcome '{review["Outcome"].Text}', refusal '{review["Refusal"].Text}'");
        DogfoodUi.Invoke(Jobs, "CloseReviewCommand");
    }

    public async Task StopAsync(JobId job)
    {
        var composer = (await SelectAsync(job))["Composer"];

        if (DogfoodUi.CanInvoke(composer, "StopCommand"))
        {
            await composer.ExecuteAsync("StopCommand");
            await journal.NoteAsync($"DRIVER STOPPED {evidence.Name(job)}");
        }
    }

    public async Task<bool> AnswerPermissionAsync(PolicyDecision decision, bool allow, string reason, DogfoodRemember remember)
    {
        var toolbar = Ui.Toolbar;
        var job = decision.Job.Match(found => found, () => default);
        bool Wanted(Bound item) => item["IsPermission"].Value<bool>() && item["Target"].Text == decision.Target && item["Job"].Value<JobId>() == job;
        Bound decisions;

        try
        {
            decisions = await OpenDecisionsAsync(toolbar, Wanted, $"the permission for {decision.Target}");
        }
        catch (TimeoutException)
        {
            await journal.NoteAsync($"NOT SHOWN: the request of {evidence.Name(job)} for '{decision.Target}' never reached the decisions popover");
            DogfoodUi.Invoke(toolbar, "CloseDecisionsCommand");

            return false;
        }

        var item = decisions["Items"].Items.First(Wanted);
        decisions.Set("Selected", item.Target);
        var offered = item["OffersAlwaysInRepository"].Value<bool>();
        evidence.Note($"offered:{decision.Target}", $"always in this repository offered {offered}: {item["AlwaysInRepositoryScope"].Text}");
        item.Set("DontAskAgain", remember != DogfoodRemember.Once);
        item.Set("AlwaysInRepository", remember == DogfoodRemember.InRepository && offered);
        await journal.DecideAsync($"{evidence.Name(job)} {decision.Kind} '{decision.Target}' asked as '{item["Title"].Text}' ({item["Asking"].Text}) -> {(allow ? "ALLOW" : "DENY")} {remember}: {reason}");
        await camera.ShootOnceAsync($"decision-{evidence.Name(job)}-{decision.Kind}", $"decisions-popover-{evidence.Name(job)}-{decision.Kind}");

        if (allow)
        {
            await decisions.ExecuteAsync("AnswerCommand");
        }
        else
        {
            DogfoodUi.Invoke(decisions, "WriteNoteCommand");
            decisions.Set("Note", reason);
            await decisions.ExecuteAsync("DenyCommand");
        }

        evidence.Note($"card:{decision.Target}", $"notice '{item["Card"]["Notice"].Text}', error '{item["Card"]["Error"].Text}'");
        DogfoodUi.Invoke(toolbar, "CloseDecisionsCommand");

        return true;
    }

    public async Task AnswerFormAsync(FormDecision form)
    {
        var toolbar = Ui.Toolbar;
        var decisions = await OpenDecisionsAsync(toolbar, item => !item["IsPermission"].Value<bool>(), "the form");
        var item = decisions["Items"].Items.First(candidate => !candidate["IsPermission"].Value<bool>());
        decisions.Set("Selected", item.Target);
        var answers = new List<string>();

        foreach (var field in item["Card"]["Fields"].Items)
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

        await journal.DecideAsync($"{evidence.Name(form.Job)} form '{item["Title"].Text}' ({form.Form.Purpose}) -> {string.Join("; ", answers)}");
        await camera.ShootOnceAsync($"decision-form-{evidence.Name(form.Job)}", $"decisions-popover-form-{evidence.Name(form.Job)}");
        await decisions.ExecuteAsync("AnswerCommand");
        DogfoodUi.Invoke(toolbar, "CloseDecisionsCommand");
    }

    private async Task<Bound> OpenDecisionsAsync(Bound toolbar, Func<Bound, bool> wanted, string what)
    {
        var decisions = toolbar["Decisions"];

        if (!toolbar["IsDecisionsOpen"].Value<bool>())
        {
            camera.OverlayPopups();
            DogfoodUi.Invoke(toolbar, "ToggleDecisionsCommand");
        }

        await Ui.UntilAsync(decisions.Value<IPresentation>(), () => decisions["Items"].Items.Any(wanted), $"{what} in the decisions popover");

        return decisions;
    }

    private async Task CandidatesAsync(Bound newJob, int candidates, string label)
    {
        if (candidates < 2)
        {
            return;
        }

        await newJob["OptionsRead"].Value<Task>();
        var options = newJob["JobOptions"];

        while (options["Candidates"].Value<int>() < candidates && DogfoodUi.CanInvoke(options, "MoreCommand"))
        {
            DogfoodUi.Invoke(options, "MoreCommand");
        }

        evidence.Note($"new-job:{label}:candidates", $"{options["CandidatesText"].Text}; more offered {DogfoodUi.CanInvoke(options, "MoreCommand")}; share '{options["Share"].Text}'; note '{options["Note"].Text}'");
    }

    private async Task ShootConnectionsAsync(string label)
    {
        var picker = app.Window.GetVisualDescendants().OfType<ComboBox>()
            .FirstOrDefault(box => box.ItemsSource is System.Collections.IEnumerable items && items.Cast<object>().Any(item => item.GetType().Name == "ConnectionOptionViewModel"));

        if (picker is null)
        {
            return;
        }

        camera.OverlayPopups();
        picker.IsDropDownOpen = true;
        await camera.ShootAsync($"{label}-new-job-connections-open");
        picker.IsDropDownOpen = false;
    }

    private static Bound? Row(Bound sidebar, JobId job) =>
        Groups.SelectMany(group => sidebar[group].Items).SelectMany(Rows).Cast<Bound?>().FirstOrDefault(row => row!.Value["Job"].Value<JobId>() == job);

    private static IEnumerable<Bound> Rows(Bound row) => [row, .. row.Has("Children") ? row["Children"].Items.SelectMany(Rows) : []];
}

internal enum DogfoodRemember
{
    Once,
    ForThisJob,
    InRepository,
}
