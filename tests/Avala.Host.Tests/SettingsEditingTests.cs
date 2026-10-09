using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class SettingsEditingTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AConnectionAddedInSettingsIsWrittenToTheDataFolderAndAJobRunsOnItAtOnceAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins);
        var machine = (await ActivatedAsync(run, "Settings"))["Machine"];
        var editor = await run.Ui.ReadAsync(() => machine["Editor"]);

        await run.Ui.InvokeAsync(() => editor.Execute("NewCommand"), Cancellation);
        await run.Ui.RunAsync(() =>
        {
            editor.Set("Name", "simulator-team");
            editor.Set("Provider", editor["Providers"].Items.Select(provider => provider.Text).ToList().FindIndex(provider => provider.EndsWith("· simulator", StringComparison.Ordinal)));

            return editor.ExecuteAsync("SaveCommand");
        });

        Assert.Contains("simulator-team", await run.Ui.ReadAsync(() => machine["Connections"].Items.Select(connection => connection["Name"].Text).ToList()));
        Assert.Contains("\"simulator-team\"", await File.ReadAllTextAsync(Path.Combine(run.DataFolder, "connections.json"), Cancellation), StringComparison.Ordinal);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply")) { Connection = new ConnectionName("simulator-team") }));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        Assert.Equal(new ConnectionName("simulator-team"), Outcomes.Present((await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Match(found => found.Summary.Connection, () => Option<ConnectionName>.None)));

        await run.Ui.InvokeAsync(() => machine["Connections"].Items.Single(connection => connection["Name"].Text == "simulator-team").Execute("RemoveCommand"), Cancellation);
        await run.Ui.RunAsync(() => editor.ExecuteAsync("RemoveCommand"));

        Assert.Equal(ConnectionError.UnknownConnection, Outcomes.FailsWith(await run.Get<IConnections>().CheckAsync(new ConnectionName("simulator-team"), Cancellation)));
    }

    [Fact]
    public async Task ARuleFileEditedInSettingsIsCheckedByItsModuleWrittenInTheWorkingTreeAndAppliesOnlyOnceCommittedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/budget.json", """{ "holdAtLimit": 0.9 }"""));
        var settings = (await ActivatedAsync(run, "Settings"))["Repository"];
        await run.Ui.RunAsync(() => settings.ExecuteAsync("OpenCommand", run.Repository.Path));
        var editor = await run.Ui.ReadAsync(() => settings["Editor"]);

        await run.Ui.RunAsync(() => settings.ExecuteAsync("EditHereCommand", settings["BudgetFile"].Target));
        await run.Ui.RunAsync(() =>
        {
            editor.Set("Content", """{ "holdAtLimit": 2 }""");

            return editor.ExecuteAsync("SaveCommand");
        });
        var rejected = await run.Ui.ReadAsync(() => editor["Error"].Text);
        await run.Ui.RunAsync(() =>
        {
            editor.Set("Content", """{ "holdAtLimit": 0.5 }""");

            return editor.ExecuteAsync("SaveCommand");
        });
        await run.Ui.RunAsync(() => settings["Refreshing"].Value<Task>());

        Assert.Equal("Not saved: jobs would reject .avala/budget.json (InvalidThreshold, from Budgets). Fix it and save again.", rejected);
        Assert.Equal("""{ "holdAtLimit": 0.5 }""", await File.ReadAllTextAsync(Path.Combine(run.Repository.Path, ".avala", "budget.json"), Cancellation));
        Assert.Equal(
            (true, "Hold when a usage window reaches", "90%"),
            await run.Ui.ReadAsync(() => (
                settings["BudgetFile"]["EditedInCheckout"].Value<bool>(),
                settings["Caps"].Items[0]["Lines"].Items[0]["Name"].Text,
                settings["Caps"].Items[0]["Lines"].Items[0]["Value"].Text)));
    }

    [Fact]
    public async Task EveryRuleFileFormatInTheApplicationAcceptsItsTemplateAndRejectsMalformedTextAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins);
        var formats = run.Get<IEnumerable<Avala.Workspaces.Contracts.IRuleFileFormat>>().ToList();

        Assert.Equal([".avala/budget.json", ".avala/checks.json", ".avala/permissions.json"], formats.Select(format => format.Path).Order(StringComparer.Ordinal));
        Assert.All(formats, format => Assert.False(format.Rejection(Templates[format.Path]).IsSome));
        Assert.All(formats, format => Assert.True(format.Rejection("{ not json").IsSome));
    }

    private static readonly Dictionary<string, string> Templates = new(StringComparer.Ordinal)
    {
        [".avala/budget.json"] = "{}",
        [".avala/checks.json"] = """{ "checks": [] }""",
        [".avala/permissions.json"] = """{ "autonomy": "supervised", "rules": [] }""",
    };

    private static async Task<Bound> ActivatedAsync(SimulatedRun run, string title)
    {
        var page = run.Page(title);
        await run.Ui.RunAsync(() =>
        {
            ((IActivatable)page.Target).Activate();

            return page.Has("Loading") ? page["Loading"].Value<Task>() : Task.CompletedTask;
        });

        return page;
    }
}
