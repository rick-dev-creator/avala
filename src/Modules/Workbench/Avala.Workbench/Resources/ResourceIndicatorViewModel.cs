using Avala.Components.Meters;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Following;
using Avala.Workbench.Presenting;
using Avala.Workbench.Upkeep;
using Avala.Workbench.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Resources;

internal interface IResourceIndicatorViewModel
{
    string Memory { get; }

    int Leftovers { get; }

    bool HasLeftovers { get; }

    IMeterViewModel Cpu { get; }

    IRelayCommand OpenCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ResourceIndicatorViewModel(ResourceReader reader, LiveFeed feed, JobFocus focus, ResourcesViewModel resources)
    : IResourceIndicatorViewModel, IActivatable, IPresentation, IDisposable
{
    private readonly MeterViewModel cpu = new("CPU", Option<double>.None);

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    [ObservableProperty]
    public partial string Memory { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLeftovers))]
    public partial int Leftovers { get; private set; }

    public bool HasLeftovers => Leftovers > 0;

    public IMeterViewModel Cpu => cpu;

    public Task Following => feed.Following;

    public void Activate() => feed.Start(_ => ValueTask.FromResult(reader.Read()), Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    private void Show(ResourceState state)
    {
        Memory = Amounts.Megabytes(state.Global.MemoryBytes);
        Leftovers = state.Leftovers;
        cpu.Show(state.Global.CpuLoad);
    }

    [RelayCommand]
    private void Open() => focus.Show(resources);
}
