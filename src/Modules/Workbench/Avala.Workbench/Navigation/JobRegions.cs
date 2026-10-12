using System.ComponentModel;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avala.Sdk.Regions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Navigation;

[INotifyPropertyChanged]
internal sealed partial class JobRegions : IDisposable
{
    private readonly IWindowLayout layout;
    private readonly IReadOnlyList<object> items;
    private double available;
    private double share = WindowLayout.DefaultPreviewShare;
    private bool restored;

    public JobRegions(IEnumerable<RegionContribution> contributions, IWindowLayout layout)
    {
        this.layout = layout;
        var contributed = contributions.ToList();
        Header = Of(WorkbenchRegions.JobHeader, contributed);
        Conversation = Of(WorkbenchRegions.Conversation, contributed);
        Preview = Of(WorkbenchRegions.Preview, contributed);
        items = [.. Header.Concat(Conversation).Concat(Preview).Distinct()];

        foreach (var pane in Preview.OfType<IPane>().OfType<INotifyPropertyChanged>())
        {
            pane.PropertyChanged += OnPaneChanged;
        }

        IsPreviewOpen = Preview.OfType<IPane>().Any(pane => pane.IsOpen);
    }

    public static JobRegions Design { get; } = new([], new FixedLayout());

    public IReadOnlyList<object> Header { get; }

    public IReadOnlyList<object> Conversation { get; }

    public IReadOnlyList<object> Preview { get; }

    [ObservableProperty]
    public partial bool IsPreviewOpen { get; private set; }

    [ObservableProperty]
    public partial double PreviewWidth { get; private set; }

    public double PreviewShare => share;

    public Task Restoring { get; private set; } = Task.CompletedTask;

    public Task Remembering { get; private set; } = Task.CompletedTask;

    public void Show(Option<JobId> job)
    {
        var context = job.Map(found => (object)found);

        foreach (var aware in items.OfType<IRegionAware>())
        {
            aware.ReceiveContext(context);
        }
    }

    public void Activate()
    {
        if (!restored)
        {
            restored = true;
            Restoring = RestoreAsync();
        }

        foreach (var item in items.OfType<IActivatable>())
        {
            item.Activate();
        }
    }

    public void Deactivate()
    {
        foreach (var item in items.OfType<IActivatable>())
        {
            item.Deactivate();
        }
    }

    public void Dispose()
    {
        foreach (var pane in Preview.OfType<IPane>().OfType<INotifyPropertyChanged>())
        {
            pane.PropertyChanged -= OnPaneChanged;
        }
    }

    [RelayCommand]
    private void Fit(double width)
    {
        available = width;
        Layout();
    }

    [RelayCommand]
    private void ResizePreview(double width)
    {
        if (available > 0)
        {
            share = WindowLayout.FitShare(width / available);
            Layout();
        }
    }

    [RelayCommand]
    private void RememberPreview()
    {
        var remembered = share;
        Remembering = layout.RememberAsync(current => current with { PreviewShare = remembered }, CancellationToken.None).AsTask();
    }

    [RelayCommand]
    private void ResetPreviewWidth()
    {
        share = WindowLayout.DefaultPreviewShare;
        Layout();
        RememberPreview();
    }

    private static List<object> Of(RegionName region, IEnumerable<RegionContribution> contributions) =>
        [.. contributions.Where(contribution => contribution.Region == region).OrderBy(contribution => contribution.Order).Select(contribution => contribution.ViewModel).Distinct()];

    private async Task RestoreAsync()
    {
        share = WindowLayout.FitShare((await layout.ReadAsync(CancellationToken.None)).PreviewShare);
        Layout();
    }

    private void Layout()
    {
        PreviewWidth = Math.Round(available * share);
        OnPropertyChanged(nameof(PreviewShare));
    }

    private void OnPaneChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IPane.IsOpen))
        {
            IsPreviewOpen = Preview.OfType<IPane>().Any(pane => pane.IsOpen);
        }
    }

    private sealed class FixedLayout : IWindowLayout
    {
        public ValueTask<WindowLayout> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(WindowLayout.Default);

        public ValueTask RememberAsync(Func<WindowLayout, WindowLayout> change, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
