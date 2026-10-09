using Avala.Sdk;

namespace Avala.Host.Tests;

public sealed class AboutTests(PublishedPlugins plugins)
{
    [Fact]
    public async Task SettingsShowsTheBuildItRunsAndOpensItsLinksAndItsLogFolderAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins);
        var settings = run.Page("Settings");
        await run.Ui.RunAsync(() =>
        {
            ((IActivatable)settings.Target).Activate();

            return settings["Loading"].Value<Task>();
        });
        var about = await run.Ui.ReadAsync(() => settings["About"]);

        var shown = await run.Ui.ReadAsync(() => (Version: about["Version"].Text, Commit: about["Commit"].Text, Folder: about["LogFolder"].Text));
        await run.Ui.RunAsync(() => about.ExecuteAsync("OpenRepositoryCommand"));
        await run.Ui.RunAsync(() => about.ExecuteAsync("OpenLogFolderCommand"));
        var note = await run.Ui.ReadAsync(() => about["Note"].Text);

        Assert.Equal(typeof(Composition.CompositionRoot).Assembly.GetName().Version!.ToString(3), shown.Version.Split('-')[0]);
        Assert.Matches("^[0-9a-f]{12}$", shown.Commit);
        Assert.Equal(new AvalaPaths(run.DataFolder).Logs, shown.Folder);
        Assert.Equal([AvalaBuild.Repository], run.Links.Opened);
        Assert.Equal($"Nothing on this computer opens folders from Avala. The log folder is {shown.Folder}.", note);
        Assert.True(Directory.Exists(shown.Folder));
    }
}
