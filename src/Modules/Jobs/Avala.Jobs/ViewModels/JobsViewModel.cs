using System.Collections.ObjectModel;
using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Jobs.ViewModels;

[INotifyPropertyChanged]
internal sealed partial class JobsViewModel : IPage
{
    public JobsViewModel() => Draft = string.Empty;

    public string Title => "Jobs";

    public ObservableCollection<string> Items { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string Draft { get; set; }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        Items.Add(Draft.Trim());
        Draft = string.Empty;
    }

    private bool CanAdd() => !string.IsNullOrWhiteSpace(Draft);
}
