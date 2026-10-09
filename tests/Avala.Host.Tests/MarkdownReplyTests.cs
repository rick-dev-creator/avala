using Avala.Components.UI.Markdown;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using MarkView.Avalonia;
using MarkView.Avalonia.Rendering;

namespace Avala.Host.Tests;

public sealed class MarkdownReplyTests(HeadlessUi ui, PublishedPlugins plugins)
{
    private const string Ending = "and [the notes](file:///etc/passwd).";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheMarkdownScenariosReplyIsDrawnAsMarkdownAndOnlyItsWebLinkOpensAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "markdown");
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => Reply(conversation) is { } reply && !reply["IsStreaming"].Value<bool>() && reply["Text"].Text.EndsWith(Ending, StringComparison.Ordinal));
        var message = await run.Ui.ReadAsync(() => Reply(conversation)!.Value.Target);
        var notice = string.Empty;
        var drawn = (Code: string.Empty, Tables: 0, Lists: 0);

        await ui.RunAsync(
            async () =>
            {
                Application.Current!.DataTemplates.Add(run.Views);

                try
                {
                    var view = ViewScript.Show(message);
                    drawn = (
                        Assert.Single(view.All<SelectableTextBlock>(), block => block.Classes.Contains("markdown-code")).Text ?? string.Empty,
                        view.All<Control>().Count(control => control.Classes.Contains("markdown-table")),
                        view.All<Control>().Count(control => control.Classes.Contains("markdown-list")));
                    await ClickAsync(view, "https://www.iso.org/iso-4217-currency-codes.html");
                    await ClickAsync(view, "file:///etc/passwd");
                    notice = view.TextOf("LinkNotice");
                }
                finally
                {
                    Application.Current.DataTemplates.Remove(run.Views);
                }
            },
            Cancellation);

        Assert.Equal(("func ToMinor(amount Money) int64 {\n    return amount.Units * pow10(amount.Currency.Exponent)\n}", 1, 1), drawn);
        Assert.Equal([new Uri("https://www.iso.org/iso-4217-currency-codes.html")], run.Links.Opened);
        Assert.Equal("Avala opens only web links, so file:///etc/passwd was not opened.", notice);
    }

    private static Bound? Reply(Bound conversation) =>
        conversation["Entries"].Items.Where(entry => entry.Kind == "MessageViewModel").Select(entry => (Bound?)entry).LastOrDefault();

    private static async Task ClickAsync(ViewScript view, string link)
    {
        view.Window.GetVisualDescendants().OfType<MarkdownViewer>().First(viewer => viewer.Name == "Settled")
            .RaiseEvent(new LinkClickedEventArgs(link) { RoutedEvent = MarkdownViewer.LinkClickedEvent });
        var command = (CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)view.Find<MarkdownText>("Text").LinkCommand;
        await (command.ExecutionTask ?? Task.CompletedTask);
        view.Settle();
    }
}
