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
