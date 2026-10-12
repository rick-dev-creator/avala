using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Submitting;

internal sealed class ProjectsPlace(IProjectsFolder projects, IFolderPicker picker)
{
    public const string Title = "Choose where your projects go";

    public async ValueTask<ProjectsFolder> ReadAsync(CancellationToken cancellationToken) => await projects.ReadAsync(cancellationToken);

    public async ValueTask<Option<string>> EnsureAsync(CancellationToken cancellationToken)
    {
        var known = await projects.ReadAsync(cancellationToken);

        return known.Chosen.IsSome ? known.Chosen : await ChooseAsync(known.Folder, cancellationToken);
    }

    public async ValueTask<Option<string>> ChangeAsync(CancellationToken cancellationToken) =>
        await ChooseAsync((await projects.ReadAsync(cancellationToken)).Folder, cancellationToken);

    public async ValueTask<Option<string>> PickAsync(string title, CancellationToken cancellationToken) =>
        await picker.PickAsync(title, (await projects.ReadAsync(cancellationToken)).Folder, cancellationToken);

    private async ValueTask<Option<string>> ChooseAsync(string near, CancellationToken cancellationToken)
    {
        var picked = await picker.PickAsync(Title, near, cancellationToken);

        return await picked.Match(
            async found => (await projects.ChooseAsync(found, cancellationToken)).Match(chosen => Option<string>.Some(chosen.Folder), _ => Option<string>.None),
            () => Task.FromResult(Option<string>.None));
    }
}
