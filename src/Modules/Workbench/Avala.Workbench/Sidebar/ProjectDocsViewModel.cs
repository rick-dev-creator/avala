using System.Collections.ObjectModel;
using Avala.Documentation.Contracts;
using Avala.Workbench.Projects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Sidebar;

internal interface IProjectDocsViewModel
{
    bool IsShown { get; }

    int Count { get; }

    bool IsOpen { get; }

    IReadOnlyList<IFeatureRowViewModel> Features { get; }

    IRelayCommand ToggleCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ProjectDocsViewModel(ProjectShelf shelf) : IProjectDocsViewModel
{
    private readonly ObservableCollection<FeatureRowViewModel> features = [];
    private string repository = string.Empty;

    public IReadOnlyList<IFeatureRowViewModel> Features => features;

    public IReadOnlyList<FeatureRowViewModel> Listed => features;

    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial int Count { get; private set; }

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    public async Task ReadAsync(string shown, CancellationToken cancellationToken)
    {
        repository = shown;
        var docs = await shelf.DocsAsync(shown, cancellationToken);
        features.Clear();

        foreach (var listed in docs.Features)
        {
            features.Add(new FeatureRowViewModel(listed, () => shelf.OpenFeature(repository, listed.Feature)));
        }

        Count = features.Count;
        IsShown = docs.Enabled && features.Count > 0;
    }

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;
}

internal interface IFeatureRowViewModel
{
    string Title { get; }

    string Fact { get; }

    IRelayCommand OpenCommand { get; }
}

internal sealed partial class FeatureRowViewModel(RepositoryFeature listed, Action open) : IFeatureRowViewModel
{
    public string Title { get; } = listed.Feature.Title;

    public string Fact { get; } = listed.Versions == 1 ? "1 version" : $"{listed.Versions} versions";

    [RelayCommand]
    private void Open() => open();
}
