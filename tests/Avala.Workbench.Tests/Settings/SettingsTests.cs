using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Testing;
using Avala.Workbench.Machine;
using Avala.Workbench.RepositoryRules;
using Avala.Workbench.Settings;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.Settings;

public sealed class SettingsTests
{
    private const string Repository = "/repositories/shop";

    private readonly FakeOpener opener = new();
    private readonly FakeSupervision supervision = new();

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
    public async Task TheMachineSettingsListTheConnectionsAndOpenTheirFileAsync()
    {
        var machine = Machine();
        await machine.LoadAsync(Cancellation);

        await machine.OpenConnectionsCommand.ExecuteAsync(null);

        Assert.Equal([("work", true, "login"), ("personal", false, "login")], machine.Connections.Select(connection => (connection.Name, connection.IsDefault, connection.Source)));
        Assert.Equal([Path.Combine("/data", "connections.json")], opener.Opened);
        Assert.Equal(("Applied", "900s", "Absent"), (machine.ConnectionsFile, machine.Silence, machine.SupervisionFile));
    }

    [Fact]
    public async Task AValidSilenceWindowIsSavedAndShownAsync()
    {
        var machine = Machine();
        machine.SilenceDraft = " 90 ";

        await machine.SaveSilenceCommand.ExecuteAsync(null);

        Assert.Equal([TimeSpan.FromSeconds(90)], supervision.Changes);
        Assert.Equal(("90s", "Applied", string.Empty), (machine.Silence, machine.SupervisionFile, machine.Error));
    }

    [Theory]
    [InlineData("soon", "Enter the window in seconds.", 0)]
    [InlineData("0", "The window must be more than 0 and at most 86,400 seconds.", 1)]
    public async Task AnInvalidSilenceWindowIsRefusedWithItsReasonAsync(string draft, string error, int changes)
    {
        var machine = Machine();
        machine.SilenceDraft = draft;

        await machine.SaveSilenceCommand.ExecuteAsync(null);

        Assert.Equal((error, changes), (machine.Error, supervision.Changes.Count));
    }

    private RepositorySettingsViewModel RepositorySettings(params JobSummary[] jobs) =>
        new(Reader(new CommittedFiles().Workspace(Repository)), new SettingsFiles(opener, new AvalaPaths("/data")), Pages.Board(jobs));

    private MachineSettingsViewModel Machine() =>
        new(new MachineSettings(new FakeConnections("work", "personal"), supervision, new FakeResources()), new SettingsFiles(opener, new AvalaPaths("/data")));

    private static RulesReader Reader(CommittedFiles files)
    {
        var rules = new FakeRules();

        return new RulesReader(rules, rules, rules, files);
    }
}
