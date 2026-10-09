using Avala.Jobs.Contracts;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Shell;

namespace Avala.Host.Tests;

internal sealed class OpenWorkbench(SimulatedRun run, Bound page, Bound sidebar, Bound toolbar)
{
    private static readonly string[] Groups = ["NeedsYou", "Running", "ReadyForReview", "Done"];

    public Bound Page => page;

    public Bound Sidebar => sidebar;

    public Bound Toolbar => toolbar;

    public static async Task<OpenWorkbench> ActivateAsync(SimulatedRun run)
    {
        var shell = run.Get<ShellViewModel>();
        await run.Ui.InvokeAsync(shell.Activate, TestContext.Current.CancellationToken);

        return new OpenWorkbench(run, run.Page("Jobs"), Region(run, ShellRegions.Sidebar).Single(), Region(run, ShellRegions.Toolbar).Single());
    }

    public Bound Section(string kind) => Region(run, ShellRegions.Inspector).Single(section => section.Kind == kind);

    public Task ShowsAsync(Func<bool> shown) =>
        run.Ui.PresentedAsync(new AnyOf([page.Presentation, sidebar.Presentation, toolbar["Decisions"].Presentation]), shown, Describe);

    public async Task<Bound> SelectAsync(JobId job)
    {
        await ShowsAsync(() => Row(job) is not null);
        await run.Ui.InvokeAsync(() => sidebar.Execute("SelectCommand", Row(job)!.Value.Target), TestContext.Current.CancellationToken);

        return await run.Ui.ReadAsync(() => page["Conversation"]);
    }

    public async Task<IReadOnlyList<Bound>> InspectAsync(JobId job, params string[] kinds)
    {
        _ = await SelectAsync(job);
        var sections = kinds.Select(Section).ToList();
        await run.Ui.InvokeAsync(() => page.Execute("ToggleInspectorCommand"), TestContext.Current.CancellationToken);

        foreach (var section in sections)
        {
            await run.Ui.PresentedAsync(section.Presentation, () => section["IsLoaded"].Value<bool>(), () => $"{section.Kind} did not load");
        }

        return sections;
    }

    public async Task<(string Group, string Fact)> RowAsync(JobId job, string group)
    {
        await ShowsInGroupAsync(job, group);

        return await run.Ui.ReadAsync(() => GroupOf(job));
    }

    public Task ShowsInGroupAsync(JobId job, string group) => ShowsAsync(() => GroupOf(job).Group == group);

    public Task ShowsInGroupAsync(JobId job, string group, string fact) => ShowsAsync(() => GroupOf(job) == (group, fact));

    public static IReadOnlyList<string> Kinds(Bound conversation) => [.. conversation["Entries"].Items.Select(entry => entry.Kind)];

    public static Bound? Entry(Bound conversation, string kind) =>
        conversation["Entries"].Items.Cast<Bound?>().FirstOrDefault(entry => entry!.Value.Kind == kind);

    public static List<string> Texts(Bound conversation, string kind, string property) =>
        [.. conversation["Entries"].Items.Where(entry => entry.Kind == kind).Select(entry => entry[property].Text)];

    private static IEnumerable<Bound> Region(SimulatedRun run, RegionName region) =>
        run.Get<IEnumerable<RegionContribution>>().Where(contribution => contribution.Region == region).Select(contribution => new Bound(contribution.ViewModel));

    private (string Group, string Fact) GroupOf(JobId job) =>
        Row(job) is { } row
            ? (Groups.Single(name => sidebar[name].Items.Any(member => member["Job"].Value<JobId>() == job)), row["Fact"].Text)
            : (string.Empty, string.Empty);

    private Bound? Row(JobId job) =>
        Groups.SelectMany(group => sidebar[group].Items).Cast<Bound?>().FirstOrDefault(row => row!.Value["Job"].Value<JobId>() == job);

    private string Describe() =>
        $"groups [{string.Join(", ", Groups.Select(group => $"{group}: {string.Join(" ", sidebar[group].Items.Select(row => row["Fact"].Text))}"))}]"
        + (page.Has("Conversation") ? $", entries [{string.Join(", ", Kinds(page["Conversation"]))}]" : string.Empty);

    private sealed class AnyOf(IReadOnlyList<IPresentation> components) : IPresentation
    {
        public long Revision => components.Sum(component => component.Revision);

        public event EventHandler<Presented>? Presented
        {
            add
            {
                foreach (var component in components)
                {
                    component.Presented += value;
                }
            }

            remove
            {
                foreach (var component in components)
                {
                    component.Presented -= value;
                }
            }
        }
    }
}
