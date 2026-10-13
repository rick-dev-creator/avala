using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Presenting;
using Avala.Workbench.Projects;
using Avala.Workbench.Sidebar;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Sidebar;

public sealed class ProjectGroupViewModelScripts
{
    private readonly FakeProjectBook book = new();
    private readonly RememberedLayout layout = new();

    [Fact]
    public async Task TogglingFoldsTheProjectAndRemembersItAsync()
    {
        var group = Group(book.Add("Side projects"));

        await group.ToggleCommand.ExecuteAsync(null);
        var folded = group.IsExpanded;
        string[] remembered = [.. layout.Remembered.CollapsedGroups];
        await group.ToggleCommand.ExecuteAsync(null);

        Assert.False(folded);
        Assert.Equal([group.Key], remembered);
        Assert.Equal((true, 0), (group.IsExpanded, layout.Remembered.CollapsedGroups.Length));
    }

    [Fact]
    public void TheDoneFoldCountsTheDoneJobsAndShowsThemOnRequest()
    {
        var group = Group(book.Add("Side projects"));
        group.ListOf(ended: true).Add(Row("Paginate the export"));
        group.ListOf(ended: true).Add(Row("Update the dependency"));

        ViewModelScript.Given(group)
            .When(shown => shown.ToggleDoneCommand.Execute(null))
            .Then(shown => Assert.Equal(("2 done", true, true, 0), (shown.DoneLabel, shown.HasDone, shown.IsDoneShown, shown.OpenCount)));
    }

    [Fact]
    public async Task RenamingSavesTheNameAndLeavesRenamingAsync()
    {
        var group = Group(book.Add("New project"));
        group.StartRenameCommand.Execute(null);
        group.Draft = "Experiments";

        await group.CommitRenameCommand.ExecuteAsync(null);

        Assert.Equal(["rename Experiments"], book.Calls);
        Assert.Equal((false, string.Empty, 1), (group.IsRenaming, group.Problem, group.Renames));
    }

    [Fact]
    public async Task ARefusedRenameSaysWhyAndKeepsRenamingAsync()
    {
        var group = Group(book.Add("New project"));
        book.Refusal = ProjectRejection.NameTaken;
        group.StartRenameCommand.Execute(null);
        group.Draft = "ledger";

        await group.CommitRenameCommand.ExecuteAsync(null);

        Assert.Equal((true, "Another project already has this name", true), (group.IsRenaming, group.Problem, group.HasProblem));
    }

    [Fact]
    public void CancellingARenameKeepsTheName()
    {
        var group = Group(book.Add("Side projects"));
        group.StartRenameCommand.Execute(null);
        group.Draft = "Something else";

        group.CancelRenameCommand.Execute(null);

        Assert.Equal((false, "Side projects", "Side projects"), (group.IsRenaming, group.Name, group.Draft));
        Assert.Empty(book.Calls);
    }

    [Fact]
    public async Task AProjectAPersonMadeCanBeDeletedButARepositorysProjectOnlyOnceEmptyAsync()
    {
        var made = Group(book.Add("Side projects"));
        made.ListOf(ended: false).Add(Row("Fix the header"));
        var repository = Group(book.Add("ledger", "/home/rick/code/ledger"));
        var emptyBefore = repository.CanDelete;
        repository.ListOf(ended: false).Add(Row("Fix the failing test"));

        await made.DeleteCommand.ExecuteAsync(null);

        Assert.Equal((true, true, false), (made.CanDelete, emptyBefore, repository.CanDelete));
        Assert.Equal(["delete Side projects"], book.Calls);
    }

    [Fact]
    public async Task ARefusedDeleteSaysWhyAsync()
    {
        var group = Group(book.Add("ledger", "/home/rick/code/ledger"));
        book.Refusal = ProjectRejection.NotEmpty;

        await group.DeleteCommand.ExecuteAsync(null);

        Assert.Equal("Move its conversations to another project first", group.Problem);
    }

    [Fact]
    public void ChatsCanBeFoldedButNeitherRenamedNorDeleted()
    {
        var chats = new ProjectGroupViewModel(new ProjectShelf(book, layout), new RelayCommand<object>(_ => { }), new GroupHeading(ProjectGroupViewModel.ChatsKey, "Chats", Option<ProjectRecord>.None, true));

        Assert.Equal((true, false, false, false), (chats.IsChats, chats.IsProject, chats.StartRenameCommand.CanExecute(null), chats.CanDelete));
    }

    private ProjectGroupViewModel Group(ProjectRecord project) =>
        new(new ProjectShelf(book, layout), new RelayCommand<object>(_ => { }), new GroupHeading(project.Project.Value.ToString(), project.Name, project, true));

    private static DesignJobRowViewModel Row(string title) =>
        new(JobId.New(), title, JobStatus.Running, "working", StatusKind.Working, 0);
}
