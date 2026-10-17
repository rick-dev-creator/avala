using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class ReadinessTests(PublishedPlugins plugins)
{
    private const string GhStatus = """
        github.com
          ✓ Logged in to github.com account octocat (keyring)
          - Active account: true
        """;

    private const string Checks = """{ "checks": [{ "arguments": ["build"] }] }""";

    private const string Approval = """{ "approval": "keep" }""";

    private const string GitHubOrigin = "https://github.com/ada/shop.git";

    private static readonly Dictionary<string, ProcessOutcome> SignedIn = new(StringComparer.Ordinal) { ["gh"] = new ProcessOutcome(0, GhStatus, string.Empty) };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARepositorysFirstJobOnGitHubWithoutAConnectionOpensWithACardOfferingTheDetectedGhLoginAndLeavesForgesJsonAloneAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [(".avala/checks.json", Checks)], signIns: SignedIn);
        await run.Repository.GitAsync(Cancellation, "remote", "add", "origin", GitHubOrigin);

        var card = await CardOfFirstJobAsync(run);

        Assert.Equal(
            [("Forge", "Connect github.com as oc••••• through gh to open pull requests and follow their checks", "Connect", false)],
            await run.Ui.ReadAsync(() => Items(card)));
        Assert.False(File.Exists(Path.Combine(run.DataFolder, "forges.json")));
    }

    [Fact]
    public async Task ConnectingFromTheCardWritesTheGhConnectionWithoutATokenAndTurnsTheItemIntoACheckMarkInPlaceAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [(".avala/checks.json", Checks)], signIns: SignedIn);
        await run.Repository.GitAsync(Cancellation, "remote", "add", "origin", GitHubOrigin);
        var card = await CardOfFirstJobAsync(run);
        var item = await run.Ui.ReadAsync(() => card["Items"].Items[0]);

        await run.Ui.RunAsync(() => item.ExecuteAsync("ActCommand"));

        Assert.Equal(string.Empty, await run.Ui.ReadAsync(() => item["Error"].Text));
        var written = await File.ReadAllTextAsync(Path.Combine(run.DataFolder, "forges.json"), Cancellation);
        Assert.Contains("\"forge\": \"github\"", written, StringComparison.Ordinal);
        Assert.Contains("\"source\": \"cli\"", written, StringComparison.Ordinal);
        Assert.DoesNotContain("reference", written, StringComparison.Ordinal);
        var shown = await run.Ui.ReadAsync(() => (card["IsShown"].Value<bool>(), Items(card)));
        Assert.True(shown.Item1);
        Assert.Equal([("Forge", "Connected github.com to open pull requests and follow their checks", string.Empty, true)], shown.Item2);
    }

    [Fact]
    public async Task WithoutAnyLoginForTheHostTheCardShowsTheSignInCommandAndChecksAgainAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [(".avala/checks.json", Checks)]);
        await run.Repository.GitAsync(Cancellation, "remote", "add", "origin", GitHubOrigin);
        var card = await CardOfFirstJobAsync(run);

        var shown = await run.Ui.ReadAsync(() => (Items(card), card["Items"].Items[0]["Command"].Text));

        Assert.Equal([("Forge", "Sign in to github.com to open pull requests and follow their checks, then check again", "Check again", false)], shown.Item1);
        Assert.Equal("gh auth login", shown.Item2);
    }

    [Fact]
    public async Task UncommittedJobRulesAreListedWithCommitWhichCommitsThatFileAloneAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [(".avala/checks.json", Checks), (".avala/jobs.json", Approval)]);
        await run.Repository.GitAsync(Cancellation, "config", "user.name", "Ada");
        await run.Repository.GitAsync(Cancellation, "config", "user.email", "ada@localhost");
        await File.WriteAllTextAsync(Path.Combine(run.Repository.Path, ".avala", "jobs.json"), """{ "approval": "merge" }""", Cancellation);
        await File.WriteAllTextAsync(Path.Combine(run.Repository.Path, "notes.txt"), "the person's own edit\n", Cancellation);
        var card = await CardOfFirstJobAsync(run);
        Assert.Equal(
            [("Rules", ".avala/jobs.json has changes jobs do not see until they are committed", "Commit", false)],
            await run.Ui.ReadAsync(() => Items(card)));

        await run.Ui.RunAsync(() => card["Items"].Items[0].ExecuteAsync("ActCommand"));

        Assert.Equal([".avala/jobs.json"], (await run.Repository.GitAsync(Cancellation, "show", "--name-only", "--format=", "HEAD")).Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal("?? notes.txt", (await run.Repository.GitAsync(Cancellation, "status", "--porcelain")).Trim());
        Assert.Equal(
            [("Rules", "Committed .avala/jobs.json, so new jobs follow it", string.Empty, true)],
            await run.Ui.ReadAsync(() => Items(card)));
    }

    [Fact]
    public async Task WithoutChecksTheCardOffersToDeclareThemWhichOpensTheirEditorInSettingsAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins);
        var card = await CardOfFirstJobAsync(run);
        Assert.Equal(
            [("Checks", "Without .avala/checks.json Avala cannot verify the work", "Declare checks", false)],
            await run.Ui.ReadAsync(() => Items(card)));

        await run.Ui.RunAsync(() => card["Items"].Items[0].ExecuteAsync("ActCommand"));

        var settings = run.Page("Settings")["Repository"];
        Assert.Equal(
            ("Settings", run.Repository.Path, true, ".avala/checks.json"),
            await run.Ui.ReadAsync(() => (run.Get<Avala.Shell.ShellViewModel>().SelectedPage?.Title, settings["Shown"].Text, settings["Editor"]["IsOpen"].Value<bool>(), settings["Editor"]["Path"].Text)));
    }

    [Fact]
    public async Task NoCardIsShownForARepositoryWithNothingMissingNorForAJobThatIsNotItsFirstAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [(".avala/checks.json", Checks)]);
        var ready = await CardOfFirstJobAsync(run, shown: false);
        Assert.False(await run.Ui.ReadAsync(() => ready["IsShown"].Value<bool>()));
        await run.Repository.GitAsync(Cancellation, "rm", "--quiet", ".avala/checks.json");
        await run.Repository.GitAsync(Cancellation, "-c", "user.name=Ada", "-c", "user.email=ada@localhost", "commit", "--quiet", "-m", "Drop the checks");

        var later = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(later);
        var card = Card(workbench);
        await run.Ui.RunAsync(() => card["Loading"].Value<Task>());

        Assert.False(await run.Ui.ReadAsync(() => card["IsShown"].Value<bool>()));
    }

    [Theory]
    [InlineData("https://git.example.org/ada/shop.git")]
    [InlineData("/srv/git/shop.git")]
    public async Task AnOriginOnAHostNoForgeSignsInToOrALocalPathShowsNoForgeItemAsync(string origin)
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [], signIns: SignedIn);
        await run.Repository.GitAsync(Cancellation, "remote", "add", "origin", origin);

        var card = await CardOfFirstJobAsync(run);

        Assert.Equal(["Checks"], await run.Ui.ReadAsync(() => Items(card).Select(item => item.Need).ToList()));
    }

    private static async Task<Bound> CardOfFirstJobAsync(SimulatedRun run, bool shown = true)
    {
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(job);
        var card = Card(workbench);
        await run.Ui.RunAsync(() => card["Loading"].Value<Task>());

        if (shown)
        {
            await run.Ui.PresentedAsync(card.Presentation, () => card["IsShown"].Value<bool>(), () => "no readiness card");
        }

        return card;
    }

    private static Bound Card(OpenWorkbench workbench) =>
        workbench.Page["Regions"]["Conversation"].Items.Single(item => item.Kind == "ReadinessCardViewModel");

    private static List<(string Need, string Text, string Action, bool IsDone)> Items(Bound card) =>
        [.. card["Items"].Items.Select(item => (item["Need"].Text, item["Text"].Text, item["Action"].Text, item["IsDone"].Value<bool>()))];
}
