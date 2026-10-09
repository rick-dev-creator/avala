using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Testing;
using Avala.Workbench.Machine;
using Avala.Workbench.RepositoryRules;
using Avala.Workbench.Settings;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.Settings;

public sealed class RepositorySettingsViewModelScripts
{
    private const string Repository = "/repositories/shop";

    private readonly FakeOpener opener = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("""{ "approval": "merge", "delegation": { "maxDepth": 2 } }""", nameof(JobFileStatus.Declared), "approval=merge|delegation={ \"maxDepth\": 2 }")]
    [InlineData("not json", nameof(JobFileStatus.Malformed), "")]
    [InlineData(null, nameof(JobFileStatus.Absent), "")]
    public async Task TheJobFileOfTheRepositorysCurrentCommitIsShownSectionBySectionAsync(string? content, string status, string sections)
    {
        var files = content is null ? new CommittedFiles().Workspace(Repository) : new CommittedFiles().With(Repository, RuleFiles.Jobs, content);

        var jobs = (await Reader(files).ReadAsync(Repository, Cancellation)).Jobs;

        Assert.Equal(
            (Enum.Parse<JobFileStatus>(status), sections, Option<FileOrigin>.Some(CommittedFiles.Origin())),
            (jobs.File, string.Join('|', jobs.Sections.Select(section => $"{section.Name}={section.Value}")), jobs.Origin));
    }

    [Fact]
    public async Task ARepositoryThatCannotBeReadHasAnUnreadableJobFileAsync()
    {
        var files = new CommittedFiles().Failing(Repository, WorkspaceFailure.NotAGitRepository);

        Assert.Equal(JobFileStatus.Unreadable, (await Reader(files).ReadAsync(Repository, Cancellation)).Jobs.File);
    }

    [Fact]
    public async Task TheRepositorysRulesAreShownReadOnlyWithEachFilesStatusAndCommitAsync()
    {
        var settings = RepositorySettings();
        settings.Repository = Repository;

        await settings.ReadCommand.ExecuteAsync(null);

        Assert.Equal(("Autonomous", "The agent's best judgment", Repository), (settings.Autonomy, settings.FormStrategy, settings.Shown));
        Assert.Equal(
            [
                (".avala/permissions.json", "Applied", "0123456"),
                (".avala/budget.json", "Rejected: Malformed", "0123456"),
                (".avala/checks.json", "Applied", "0123456"),
                (".avala/jobs.json", "Absent", "ba5eba5"),
            ],
            settings.Files.Select(file => (file.Path, file.Status, file.Commit)));
        Assert.Equal([("Built-in", "policy-file-goes-to-a-human"), ("Repository", "tests")], settings.Rules.Select(rule => (rule.Origin, rule.Name)));
        Assert.Equal([("Every connection", "no caps"), ("work", "2 USD per job, holds at 90% of a limit")], settings.Caps.Select(caps => (caps.Scope, caps.Caps)));
        Assert.Equal([("build", "dotnet build", "90s")], settings.Checks.Select(check => (check.Name, check.Command, check.Timeout)));
    }

    [Fact]
    public async Task LoadingReadsTheRepositoryOfTheLatestJobAsync()
    {
        var settings = RepositorySettings(Pages.Summary("Fix the failing test", JobStatus.Running));

        await settings.LoadAsync(Cancellation);

        Assert.Equal([Repository], settings.Repositories);
        Assert.Equal(Repository, settings.Shown);
    }

    [Fact]
    public async Task EditingARuleFileAsksThePlatformToOpenItInTheRepositoryAndShowsARefusalAsync()
    {
        var settings = RepositorySettings();
        settings.Repository = Repository;
        await settings.ReadCommand.ExecuteAsync(null);

        await settings.EditCommand.ExecuteAsync(settings.Files[0]);
        opener.Refusal = FileOpenError.Refused;
        await settings.EditCommand.ExecuteAsync(settings.Files[3]);

        Assert.Equal([Path.Combine(Repository, ".avala/permissions.json"), Path.Combine(Repository, ".avala/jobs.json")], opener.Opened);
        Assert.Equal(($"The platform refused to open {Path.Combine(Repository, ".avala/jobs.json")}.", string.Empty), (settings.Error, settings.Notice));
    }

    [Fact]
    public async Task EditingARuleFileTheRepositoryLacksCreatesItFromAMinimalTemplateAndSaysItAppliesOnceCommittedAsync()
    {
        var settings = RepositorySettings();
        settings.Repository = Repository;
        await settings.ReadCommand.ExecuteAsync(null);

        await settings.EditCommand.ExecuteAsync(settings.Files[0]);
        var created = (settings.Error, settings.Notice);
        await settings.EditCommand.ExecuteAsync(settings.Files[0]);

        Assert.Equal(
            (string.Empty, ".avala/permissions.json did not exist, so it was created from a minimal template in the repository's working tree. Jobs read it once it is committed."),
            created);
        Assert.Equal("{\n  \"autonomy\": \"supervised\",\n  \"rules\": []\n}\n", opener.Created[Path.Combine(Repository, ".avala/permissions.json")]);
        Assert.Equal(string.Empty, settings.Notice);
    }

    [Fact]
    public void NothingCanBeReadWithoutARepositoryNorEditedBeforeARead() =>
        ViewModelScript.Given(RepositorySettings())
            .When(settings => settings.Repository = "  ")
            .Then(settings => Assert.Equal((false, false), (settings.ReadCommand.CanExecute(null), settings.EditCommand.CanExecute(new DesignRuleFileViewModel()))));

    [Fact]
    public async Task TypingAnotherRepositoryKeepsShowingTheRulesReadUntilItIsRead()
    {
        var settings = RepositorySettings();
        settings.Repository = Repository;
        await settings.ReadCommand.ExecuteAsync(null);

        settings.Repository = "/repositories/web";

        Assert.Equal((Repository, 4), (settings.Shown, settings.Files.Count));
    }

    [Fact]
    public async Task ChoosingARepositoryReadsItsRulesInDecisionOrderWithEachSectionsFileAsync()
    {
        var settings = RepositorySettings();

        await settings.OpenCommand.ExecuteAsync(Repository);

        Assert.Equal((Repository, Repository, "shop"), (settings.Repository, settings.Shown, settings.Name));
        Assert.Equal([1, 2], settings.Rules.Select(rule => rule.Order));
        Assert.Equal((".avala/permissions.json", ".avala/budget.json", true), (settings.PermissionsFile?.Path, settings.BudgetFile?.Path, settings.BudgetFile?.IsRejected));
        Assert.Equal("Autonomous, the agent's best judgment, 2 rules", settings.Files[0].Summary);
        Assert.Equal("build", settings.Files[2].Summary);
    }

    [Fact]
    public async Task ChoosingNoRepositoryReadsNothingAsync()
    {
        var settings = RepositorySettings();

        await settings.OpenCommand.ExecuteAsync("  ");

        Assert.Equal((string.Empty, 0), (settings.Shown, settings.Files.Count));
    }

    [Fact]
    public async Task EditingARuleFileHereStartsFromTheWorkingCopyOrATemplateAndSaysWhenItAppliesAsync()
    {
        var settings = RepositorySettings();
        working.Files[RuleFiles.Budget] = "{ \"holdAtLimit\": 0.9 }";
        await settings.OpenCommand.ExecuteAsync(Repository);

        await settings.EditHereCommand.ExecuteAsync(settings.BudgetFile);
        var existing = (settings.Editor.IsOpen, settings.Editor.Path, settings.Editor.Content);
        await settings.EditHereCommand.ExecuteAsync(settings.PermissionsFile);

        Assert.Equal((true, RuleFiles.Budget, "{ \"holdAtLimit\": 0.9 }"), existing);
        Assert.Equal(SettingsFiles.TemplateOf(RuleFiles.Permissions), settings.Editor.Content);
        Assert.StartsWith($"{RuleFiles.Permissions} does not exist yet", settings.Editor.Note, StringComparison.Ordinal);
        Assert.Contains("applies to new jobs once you commit it", settings.Editor.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFileTheModulesParserRejectsIsNeverWrittenAndSaysWhyAsync()
    {
        var settings = RepositorySettings();
        await settings.OpenCommand.ExecuteAsync(Repository);
        await settings.EditHereCommand.ExecuteAsync(settings.BudgetFile);

        settings.Editor.Content = "{ \"holdAtLimit\": 2 }";
        await settings.Editor.SaveCommand.ExecuteAsync(null);

        Assert.Empty(working.Written);
        Assert.Equal((true, "Not saved: jobs would reject .avala/budget.json (InvalidThreshold, from Budgets). Fix it and save again."), (settings.Editor.IsOpen, settings.Editor.Error));
    }

    [Fact]
    public async Task AValidFileIsWrittenInTheWorkingTreeAndThePageReadsTheRepositoryAgainAsync()
    {
        var settings = RepositorySettings();
        await settings.OpenCommand.ExecuteAsync(Repository);
        await settings.EditHereCommand.ExecuteAsync(settings.BudgetFile);

        settings.Editor.Content = "{ \"holdAtLimit\": 0.8 }";
        await settings.Editor.SaveCommand.ExecuteAsync(null);
        await settings.Refreshing;

        Assert.Equal([RuleFiles.Budget], working.Written);
        Assert.Equal((false, "Saved .avala/budget.json in the repository's working tree. It applies to new jobs once you commit it."), (settings.Editor.IsOpen, settings.Notice));
    }

    [Fact]
    public async Task OnlyAFileWithAKnownFormatCanBeEditedHereAsync()
    {
        var settings = RepositorySettings();

        await settings.OpenCommand.ExecuteAsync(Repository);

        Assert.Equal(
            [(RuleFiles.Permissions, false), (RuleFiles.Budget, true), (RuleFiles.Checks, false), (RuleFiles.Jobs, false)],
            settings.Files.Select(file => (file.Path, file.CanEditHere)));
        Assert.False(settings.EditHereCommand.CanExecute(settings.Files[3]));
    }

    [Fact]
    public async Task AWorkingTreeThatCannotBeWrittenKeepsTheEditorOpenWithTheReasonAsync()
    {
        var settings = RepositorySettings();
        await settings.OpenCommand.ExecuteAsync(Repository);
        await settings.EditHereCommand.ExecuteAsync(settings.BudgetFile);
        working.Refusal = WorkspaceFailure.FileUnwritable;

        await settings.Editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal((true, ".avala/budget.json could not be written."), (settings.Editor.IsOpen, settings.Editor.Error));
    }

    private readonly FakeWorkingFiles working = new();

    private RepositorySettingsViewModel RepositorySettings(params JobSummary[] jobs) =>
        new(
            Reader(new CommittedFiles().Workspace(Repository)),
            new SettingsFiles(opener, new AvalaPaths("/data")),
            Pages.Board(jobs),
            new RuleFileEditorViewModel(new RuleFileEditing(working, [new BudgetLikeFormat()])));

    private static RulesReader Reader(CommittedFiles files)
    {
        var rules = new FakeRules();

        return new RulesReader(rules, rules, rules, files);
    }
}

public sealed class RuleFileEditorViewModelScripts
{
    private readonly FakeWorkingFiles working = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CancellingClosesTheEditorWithoutWritingAndClearsItsErrorAsync()
    {
        var editor = new RuleFileEditorViewModel(new RuleFileEditing(working, [new BudgetLikeFormat()]));
        await editor.OpenAsync("/repositories/shop", RuleFiles.Budget, Cancellation);
        editor.Content = "{ \"holdAtLimit\": 2 }";
        await editor.SaveCommand.ExecuteAsync(null);
        var refused = editor.Error.Length > 0;

        editor.CancelCommand.Execute(null);

        Assert.True(refused);
        Assert.Equal((false, string.Empty, false), (editor.IsOpen, editor.Error, editor.SaveCommand.CanExecute(null)));
        Assert.Empty(working.Written);
    }

    [Fact]
    public async Task EmptyTextCannotBeSavedAsync()
    {
        var editor = new RuleFileEditorViewModel(new RuleFileEditing(working, [new BudgetLikeFormat()]));
        await editor.OpenAsync("/repositories/shop", RuleFiles.Budget, Cancellation);

        ViewModelScript.Given(editor)
            .When(opened => opened.Content = "   ")
            .Then(opened => Assert.False(opened.SaveCommand.CanExecute(null)));
    }

    [Fact]
    public async Task AWorkingTreeThatCannotBeReadOpensNothingAndSaysWhyAsync()
    {
        working.Refusal = WorkspaceFailure.NotAGitRepository;
        var editor = new RuleFileEditorViewModel(new RuleFileEditing(working, [new BudgetLikeFormat()]));

        await editor.OpenAsync("/repositories/shop", RuleFiles.Budget, Cancellation);

        Assert.Equal((false, "This folder is not a git repository."), (editor.IsOpen, editor.Error));
    }
}
