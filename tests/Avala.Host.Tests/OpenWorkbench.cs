using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Host.Tests;

internal sealed class OpenWorkbench(SimulatedRun run, Bound page)
{
    private static readonly string[] Groups = ["NeedsYou", "Running", "ReadyForReview", "Done"];

    public Bound Page => page;

    public static async Task<OpenWorkbench> ActivateAsync(SimulatedRun run)
    {
        var page = run.Page("Jobs");
        await run.Ui.InvokeAsync(() => ((IActivatable)page.Target).Activate(), TestContext.Current.CancellationToken);

        return new OpenWorkbench(run, page);
    }

    public Task ShowsAsync(Func<bool> shown) => run.Ui.PresentedAsync(page.Presentation, shown, Describe);

    public async Task<Bound> SelectAsync(JobId job)
    {
        await ShowsAsync(() => Row(job) is not null);
        await run.Ui.InvokeAsync(() => page["Sidebar"].Execute("SelectCommand", Row(job)!.Value.Target), TestContext.Current.CancellationToken);

        return await run.Ui.ReadAsync(() => page["Conversation"]);
    }

    public Task ShowsInGroupAsync(JobId job, string group) => ShowsAsync(() => GroupOf(job).Group == group);

    public Task ShowsInGroupAsync(JobId job, string group, string fact) => ShowsAsync(() => GroupOf(job) == (group, fact));

    public static IReadOnlyList<string> Kinds(Bound conversation) => [.. conversation["Entries"].Items.Select(entry => entry.Kind)];

    public static Bound? Entry(Bound conversation, string kind) =>
        conversation["Entries"].Items.Cast<Bound?>().FirstOrDefault(entry => entry!.Value.Kind == kind);

    public static List<string> Texts(Bound conversation, string kind, string property) =>
        [.. conversation["Entries"].Items.Where(entry => entry.Kind == kind).Select(entry => entry[property].Text)];

    private (string Group, string Fact) GroupOf(JobId job) =>
        Row(job) is { } row
            ? (Groups.Single(name => page["Sidebar"][name].Items.Any(member => member["Job"].Value<JobId>() == job)), row["Fact"].Text)
            : (string.Empty, string.Empty);

    private Bound? Row(JobId job) =>
        Groups.SelectMany(group => page["Sidebar"][group].Items).Cast<Bound?>().FirstOrDefault(row => row!.Value["Job"].Value<JobId>() == job);

    private string Describe() =>
        $"groups [{string.Join(", ", Groups.Select(group => $"{group}: {string.Join(" ", page["Sidebar"][group].Items.Select(row => row["Fact"].Text))}"))}]"
        + (page.Has("Conversation") ? $", entries [{string.Join(", ", Kinds(page["Conversation"]))}]" : string.Empty);
}
