using Avala.Sdk;
using Avala.Workbench.Following;
using Avala.Workbench.Presenting;
using Avala.Workbench.Upkeep;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Resources;

[INotifyPropertyChanged]
internal sealed partial class ResourceIndicatorViewModel(ResourceReader reader, LiveFeed feed) : IActivatable, IDisposable
{
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
