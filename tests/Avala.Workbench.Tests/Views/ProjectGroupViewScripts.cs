using Avala.Components.Status;
using Avala.Components.UI.Copying;
using Avala.Jobs.Contracts;
using Avala.Testing.UI;
using Avala.Workbench.Board;
using Avala.Workbench.Presenting;
using Avala.Workbench.Sidebar;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class ProjectGroupViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AnOpenProjectShowsItsFolderNameAndOpenRowsWithTheirCountAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignProjectGroupViewModel { Open = [Row("Fix JPY rounding"), Row("Add invoice PDF endpoint")] });

            Assert.Equal(("acme-dashboard", "2"), (view.TextOf("ProjectName"), view.TextOf("Count")));
            Assert.Equal((true, true, true, false, false), (view.Shows("Folder"), view.Shows("Expanded"), view.Shows("Open"), view.Shows("Attention"), view.Shows("ShowDone")));
        }, Cancellation);

    [Fact]
    public Task AFoldedProjectHidesItsRowsAndRingsWhenOneOfItsJobsNeedsYouAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignProjectGroupViewModel { IsExpanded = false, NeedsYou = true, Open = [Row("Fix the calculator")] });

            Assert.Equal((true, false, false, true), (view.Shows("Collapsed"), view.Shows("Expanded"), view.Shows("Open"), view.Shows("Attention")));
        }, Cancellation);

    [Fact]
    public Task ClickingTheHeaderFoldsTheProjectAsync() =>
        ui.RunAsync(() =>
        {
            var toggled = 0;
            var view = Screen.Show(new DesignProjectGroupViewModel { ToggleCommand = new AsyncRelayCommand(() => { toggled++; return Task.CompletedTask; }) });

            view.Click("Header");

            Assert.Equal(1, toggled);
        }, Cancellation);

    [Fact]
    public Task RenamingShowsTheNameFieldWhereEnterSavesAndEscapeCancelsAsync() =>
        ui.RunAsync(() =>
        {
            var saved = 0;
            var cancelled = 0;
            var view = Screen.Show(new DesignProjectGroupViewModel
            {
                IsRenaming = true,
                CommitRenameCommand = new AsyncRelayCommand(() => { saved++; return Task.CompletedTask; }),
                CancelRenameCommand = new RelayCommand(() => cancelled++),
            });
            var shown = (view.Shows("Draft"), view.Shows("Header"));

            view.Find("Draft").Focus();
            view.Press(Key.Enter).Press(Key.Escape);

            Assert.Equal((true, false), shown);
            Assert.Equal((1, 1), (saved, cancelled));
        }, Cancellation);

    [Fact]
    public Task TheDoneFoldShowsHowManyAreDoneAndListsThemWhenShownAsync() =>
        ui.RunAsync(() =>
        {
            var folded = Screen.Show(new DesignProjectGroupViewModel { Done = [Row("Paginate the export", JobGroup.Done)] });
            var foldedReading = (folded.TextOf("DoneLabel"), folded.Shows("Done"));
            var unfolded = Screen.Show(new DesignProjectGroupViewModel { Done = [Row("Paginate the export", JobGroup.Done)], IsDoneShown = true });

            Assert.Equal(("1 done", false), foldedReading);
            Assert.True(unfolded.Shows("Done"));
        }, Cancellation);

    [Fact]
    public Task ChatsShowNeitherAFolderNorACountAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignProjectGroupViewModel { Key = "chats", Name = "Chats", IsChats = true, IsProject = false, Open = [Row("Sketch a CSV parser")] });

            Assert.Equal(("Chats", false, false), (view.TextOf("ProjectName"), view.Shows("Folder"), view.Shows("Count")));
        }, Cancellation);

    [Fact]
    public Task OnlyAnArchivableRowOffersTheArchiveGlyphAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignProjectGroupViewModel { Open = [Row("Fix the calculator"), Row("Update the dependency") with { CanArchive = true }] });

            Assert.Equal(1, Archives(view).Count(glyph => glyph.IsEffectivelyVisible));
        }, Cancellation);

    [Fact]
    public Task TheArchiveGlyphAndTheShortcutOnTheSelectedRowArchiveItAsync() =>
        ui.RunAsync(() =>
        {
            var archived = new List<string>();
            var view = Screen.Show(new DesignProjectGroupViewModel
            {
                Open =
                [
                    Row("Update the dependency") with { CanArchive = true, IsSelected = false, ArchiveCommand = Archiving("clicked", archived) },
                    Row("Paginate the export") with { CanArchive = true, IsSelected = true, ArchiveCommand = Archiving("pressed", archived) },
                ],
            });

            view.Click(Archives(view)[0]).Press(Hotkeys.Archive);

            Assert.Equal(["clicked", "pressed"], archived);
        }, Cancellation);

    [Fact]
    public Task ArchiveDoneArchivesTheDoneRowsFromItsFoldAsync() =>
        ui.RunAsync(() =>
        {
            var archived = new List<string>();
            var view = Screen.Show(new DesignProjectGroupViewModel { Done = [Row("Paginate the export", JobGroup.Done)], ArchiveDoneCommand = Archiving("done", archived) });

            view.Click("ArchiveDone");

            Assert.Equal(["done"], archived);
        }, Cancellation);

    [Fact]
    public Task ARefusedArchiveSaysWhyUnderItsRowAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignProjectGroupViewModel { Open = [Row("Update the dependency") with { Refusal = "It needs you before it can be archived" }] });

            Assert.Equal("It needs you before it can be archived", view.TextOf("Refusal"));
        }, Cancellation);

    private static List<Button> Archives(ViewScript view) =>
        [.. view.All<Button>().Where(button => AutomationProperties.GetName(button) == "Archive")];

    private static AsyncRelayCommand Archiving(string name, List<string> archived) =>
        new(() =>
        {
            archived.Add(name);

            return Task.CompletedTask;
        });

    private static DesignJobRowViewModel Row(string title, JobGroup group = JobGroup.Running) =>
        new(JobId.New(), title, JobStatus.Running, "working", StatusKind.Working, 0) { Group = group };
}
