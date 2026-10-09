using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Components.Canvases;

[INotifyPropertyChanged]
public sealed partial class CanvasSurfaceViewModel : ICanvasSurfaceViewModel
{
    public const int VersionLimit = 24;

    private readonly List<CanvasRendering> versions = [];
    private readonly Guid surface = Guid.NewGuid();
    private int sequence;

    public CanvasSurfaceViewModel()
    {
        Title = string.Empty;
        MediaLabel = string.Empty;
        StatusText = string.Empty;
        VersionText = string.Empty;
    }

    [ObservableProperty]
    public partial string Title { get; private set; }

    [ObservableProperty]
    public partial string MediaLabel { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; }

    [ObservableProperty]
    public partial bool IsStreaming { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    public partial CanvasRendering Shown { get; private set; } = CanvasRendering.Nothing;

    [ObservableProperty]
    public partial int VersionCount { get; private set; }

    [ObservableProperty]
    public partial string VersionText { get; private set; }

    [ObservableProperty]
    public partial bool HasVersions { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LatestVersionCommand))]
    public partial bool IsFollowingLatest { get; private set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(CloseCommand))]
    public partial bool IsOpen { get; private set; }

    private int ShownIndex => versions.IndexOf(Shown);

    public void Show(CanvasDraft draft)
    {
        Title = draft.Title;
        MediaLabel = CanvasMediaTypes.Label(draft.MediaType);
        IsStreaming = draft.Phase == CanvasPhase.Streaming;
        StatusText = Status(draft.Phase);
        Record(draft);
    }

    [RelayCommand(CanExecute = nameof(CanStepBack))]
    private void PreviousVersion() => ShowVersion(ShownIndex - 1);

    private bool CanStepBack() => ShownIndex > 0;

    [RelayCommand(CanExecute = nameof(CanStepForward))]
    private void NextVersion() => ShowVersion(ShownIndex + 1);

    private bool CanStepForward() => ShownIndex >= 0 && ShownIndex < versions.Count - 1;

    [RelayCommand(CanExecute = nameof(CanReturnToLatest))]
    private void LatestVersion() => ShowVersion(versions.Count - 1);

    private bool CanReturnToLatest() => !IsFollowingLatest;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open() => IsOpen = true;

    private bool CanOpen() => !IsOpen && !Shown.IsNothing;

    [RelayCommand(CanExecute = nameof(IsOpen))]
    private void Close() => IsOpen = false;

    private void Record(CanvasDraft draft)
    {
        var isFinal = draft.Phase != CanvasPhase.Streaming;

        if (versions.Count > 0 && versions[^1].Content == draft.Content && versions[^1].MediaType == draft.MediaType)
        {
            if (versions[^1].IsFinal != isFinal)
            {
                Replace(versions.Count - 1, versions[^1] with { IsFinal = isFinal, Version = ++sequence });
            }

            return;
        }

        versions.Add(new CanvasRendering(draft.MediaType, draft.Content, isFinal, ++sequence) { Surface = surface });

        if (versions.Count > VersionLimit)
        {
            versions.RemoveAt(Shown == versions[0] ? 1 : 0);
        }

        if (IsFollowingLatest)
        {
            ShowVersion(versions.Count - 1);
        }
        else
        {
            Refresh();
        }
    }

    private void Replace(int index, CanvasRendering rendering)
    {
        var wasShown = ShownIndex == index;
        versions[index] = rendering;

        if (wasShown)
        {
            ShowVersion(index);
        }
    }

    private void ShowVersion(int index)
    {
        Shown = versions[Math.Clamp(index, 0, versions.Count - 1)];
        Refresh();
    }

    private void Refresh()
    {
        var index = ShownIndex;
        VersionCount = versions.Count;
        HasVersions = versions.Count > 1;
        IsFollowingLatest = index == versions.Count - 1;
        VersionText = string.Create(CultureInfo.InvariantCulture, $"{index + 1} of {versions.Count}");
        PreviousVersionCommand.NotifyCanExecuteChanged();
        NextVersionCommand.NotifyCanExecuteChanged();
    }

    private static string Status(CanvasPhase phase) => phase switch
    {
        CanvasPhase.Streaming => "Drawing",
        CanvasPhase.Failed => "Failed",
        CanvasPhase.Stopped => "Stopped",
        _ => string.Empty,
    };
}
