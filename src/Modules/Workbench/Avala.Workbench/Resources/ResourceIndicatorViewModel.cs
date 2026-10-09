using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Following;
using Avala.Workbench.Presenting;
using Avala.Workbench.Upkeep;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Resources;

internal interface IResourceIndicatorViewModel
{
    string Memory { get; }

    int Leftovers { get; }

    bool HasLeftovers { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ResourceIndicatorViewModel(ResourceReader reader, LiveFeed feed) : IResourceIndicatorViewModel, IActivatable, IPresentation, IDisposable
{
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

    public Task Following => feed.Following;

    public void Activate() => feed.Start(_ => ValueTask.FromResult(reader.Read()), Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    private void Show(ResourceState state)
    {
        Memory = Amounts.Megabytes(state.Global.MemoryBytes);
        Leftovers = state.Leftovers;
    }
}
