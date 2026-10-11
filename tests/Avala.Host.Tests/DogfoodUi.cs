using System.Diagnostics;
using Avala.Host.Composition;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Shell.Regions;
using Microsoft.Extensions.DependencyInjection;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal sealed class DogfoodUi(DogfoodJournal journal, DogfoodCamera camera)
{
    public CompositionRoot Root { private get; set; } = null!;

    public Bound Toolbar => Region(ShellRegions.Toolbar).Single();

    public Bound Sidebar => Region(ShellRegions.Sidebar).Single();

    public Bound Jobs => Page("Jobs");

    public IEnumerable<Bound> Region(RegionName region) =>
        Root.Services.GetServices<RegionContribution>().Where(contribution => contribution.Region == region).OrderBy(contribution => contribution.Order).Select(contribution => new Bound(contribution.ViewModel));

    public Bound Page(string title) => new(Assert.Single(Root.Services.GetServices<IPage>(), page => page.Title == title));

    public static void Invoke(Bound target, string command, object? parameter = null) =>
        ((System.Windows.Input.ICommand)target[command].Target).Execute(parameter);

    public static bool CanInvoke(Bound target, string command) =>
        ((System.Windows.Input.ICommand)target[command].Target).CanExecute(null);

    public async Task UntilAsync(IPresentation presentation, Func<bool> shown, string what)
    {
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waited = Stopwatch.StartNew();

        void Check(object? sender, Presented presented)
        {
            if (Holds(shown))
            {
                seen.TrySetResult();
            }
        }

        presentation.Presented += Check;

        try
        {
            if (!Holds(shown))
            {
                await seen.Task.WaitAsync(TimeSpan.FromMinutes(2), Cancellation);
            }
        }
        catch (TimeoutException)
        {
            await journal.NoteAsync($"FINDING: waited 2 minutes for {what} and it never showed");
            await camera.ShootAsync($"zz-never-showed-{what.Replace(' ', '-')}");
            throw;
        }
        finally
        {
            presentation.Presented -= Check;
        }

        if (waited.Elapsed > TimeSpan.FromSeconds(5))
        {
            await journal.NoteAsync($"SLOW: {what} took {waited.Elapsed.TotalSeconds:F1}s to show");
        }
    }

    private static bool Holds(Func<bool> shown)
    {
        try
        {
            return shown();
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
