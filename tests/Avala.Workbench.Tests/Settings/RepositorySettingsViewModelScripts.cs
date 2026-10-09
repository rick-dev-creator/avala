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
        opener.Refusal = FileOpenError.NotFound;
        await settings.EditCommand.ExecuteAsync(settings.Files[3]);

        Assert.Equal([Path.Combine(Repository, ".avala/permissions.json"), Path.Combine(Repository, ".avala/jobs.json")], opener.Opened);
        Assert.Equal("The file does not exist in the checkout yet.", settings.Error);
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

    private RepositorySettingsViewModel RepositorySettings(params JobSummary[] jobs) =>
        new(Reader(new CommittedFiles().Workspace(Repository)), new SettingsFiles(opener, new AvalaPaths("/data")), Pages.Board(jobs));

    private static RulesReader Reader(CommittedFiles files)
    {
        var rules = new FakeRules();

        return new RulesReader(rules, rules, rules, files);
    }
}
