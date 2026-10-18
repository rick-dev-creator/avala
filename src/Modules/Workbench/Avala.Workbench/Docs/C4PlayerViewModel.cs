using System.Collections.ObjectModel;
using Avala.Components.Graphs;
using Avala.Components.Zooming;
using Avala.Documentation.Contracts;
using Avala.Sdk;
using Avala.Workbench.Documenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Docs;

internal interface IC4PlayerViewModel
{
    bool IsShown { get; }

    bool IsCompact { get; }

    string Title { get; }

    string Lead { get; }

    string Path { get; }

    IReadOnlyList<string> Versions { get; }

    int Version { get; set; }

    IReadOnlyList<IC4NodeViewModel> Nodes { get; }

    IReadOnlyList<GraphLink> Links { get; }

    IReadOnlyList<GraphFrame> Frames { get; }

    IReadOnlyList<GraphLink> Structure { get; }

    IReadOnlyList<IDocSectionViewModel> Sections { get; }

    IReadOnlyList<Conversation.ICanvasViewModel> Unread { get; }

    bool IsDrawn { get; }

    string Caption { get; }

    string Position { get; }

    string Speed { get; }

    bool IsPlaying { get; }

    IRelayCommand PlayCommand { get; }

    IRelayCommand StopCommand { get; }

    IRelayCommand BackCommand { get; }

    IRelayCommand NextCommand { get; }

    IRelayCommand SpeedCommand { get; }

    IZoomViewModel Zoom { get; }
}

[INotifyPropertyChanged]
internal sealed partial class C4PlayerViewModel(TimeProvider time, IUiDispatcher dispatcher, IZoomViewModel zoom) : IC4PlayerViewModel, IDisposable
{
    private static readonly double[] Speeds = [1, 1.5, 2, 0.5];
    private static readonly TimeSpan StepPause = TimeSpan.FromSeconds(1.9);
    private static readonly TimeSpan HopPause = TimeSpan.FromSeconds(1.4);

    private readonly ObservableCollection<C4NodeViewModel> nodes = [];
    private readonly ObservableCollection<DocSectionViewModel> sections = [];
    private Option<FeatureStory> story;
    private int step = -1;
    private int hop;
    private int speed;
    private ITimer? timer;
    private bool advancing;

    public IReadOnlyList<IC4NodeViewModel> Nodes => nodes;

    public IReadOnlyList<IDocSectionViewModel> Sections => sections;

    public IZoomViewModel Zoom => zoom;

    public IReadOnlyList<DocSectionViewModel> Shown => sections;

    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial bool IsCompact { get; set; }

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Lead { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Path { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> Versions { get; private set; } = [];

    [ObservableProperty]
    public partial int Version { get; set; } = -1;

    [ObservableProperty]
    public partial IReadOnlyList<GraphLink> Links { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<GraphFrame> Frames { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<GraphLink> Structure { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<Conversation.ICanvasViewModel> Unread { get; private set; } = [];

    [ObservableProperty]
    public partial bool IsDrawn { get; private set; }

    [ObservableProperty]
    public partial string Caption { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Position { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Speed { get; private set; } = DocsPhrases.Speed(1);

    [ObservableProperty]
    public partial bool IsPlaying { get; private set; }

    public Task Ticking { get; private set; } = Task.CompletedTask;

    private IReadOnlyList<PlayStep> Steps => story.Match(found => Version >= 0 && Version < found.Versions.Count ? found.Versions[Version].Steps : [], () => []);

    public void Show(Option<FeatureStory> shown)
    {
        Pause();
        story = shown;
        IsShown = shown.IsSome;
        Versions = shown.Match(found => (IReadOnlyList<string>)[.. found.Versions.Select(DocsPhrases.Version)], () => []);
        var last = Versions.Count - 1;

        if (Version == last)
        {
            OnVersionChanged(last);
        }
        else
        {
            Version = last;
        }
    }

    public void GoTo(string anchor)
    {
        Pause();
        var index = Steps.Select((shown, at) => (shown, at)).FirstOrDefault(pair => pair.shown.Anchor == anchor, (null!, -1)).Item2;

        if (index >= 0)
        {
            step = index;
            hop = 0;
            Render();
        }
    }

    public void Dispose() => Pause();

    partial void OnVersionChanged(int value)
    {
        if (advancing)
        {
            step = -1;
            hop = 0;
        }
        else
        {
            Pause();
            Rest();
        }

        Rebuild();
        Render();
    }

    [RelayCommand]
    private void Play()
    {
        if (IsPlaying)
        {
            Pause();

            return;
        }

        if (AtEnd())
        {
            Version = 0;
            step = -1;
            hop = 0;
            Render();
        }

        IsPlaying = true;
        Arm();
    }

    [RelayCommand]
    private void Stop()
    {
        Pause();
        step = -1;
        hop = 0;
        Render();
    }

    [RelayCommand]
    private void Back()
    {
        Pause();

        if (hop > 0)
        {
            hop--;
        }
        else if (step >= 0)
        {
            step--;
        }

        Render();
    }

    [RelayCommand]
    private void Next()
    {
        Pause();
        Advance();
    }

    [RelayCommand]
    private void ChangeSpeed()
    {
        speed = (speed + 1) % Speeds.Length;
        Speed = DocsPhrases.Speed(Speeds[speed]);
    }

    public IRelayCommand SpeedCommand => ChangeSpeedCommand;

    private bool Advance()
    {
        var steps = Steps;
        var now = step >= 0 && step < steps.Count ? steps[step] : null;

        if (now is { Kind: StepKind.Flow } && hop < now.Relations.Count - 1)
        {
            hop++;
        }
        else if (step < steps.Count - 1)
        {
            step++;
            hop = 0;
        }
        else if (Version < Versions.Count - 1)
        {
            advancing = true;
            Version++;
            advancing = false;

            return true;
        }
        else
        {
            return false;
        }

        Render();

        return true;
    }

    private bool AtEnd() => Version == Versions.Count - 1 && step >= Steps.Count - 1;

    private void Rest()
    {
        var steps = Steps;
        var settled = steps.Select((shown, index) => (shown, index)).LastOrDefault(pair => pair.shown.Kind != StepKind.Flow, (null!, -1)).Item2;
        step = settled >= 0 ? settled : steps.Count - 1;
        hop = 0;
    }

    private void Rebuild()
    {
        var played = story.Bind(found => Version >= 0 && Version < found.Versions.Count ? Option<PlayedVersion>.Some(found.Versions[Version]) : Option<PlayedVersion>.None);
        Title = played.Match(found => found.Document.Title, () => string.Empty);
        Lead = played.Match(found => found.Document.Lead, () => string.Empty);
        Path = played.Match(found => found.File, () => string.Empty);
        IsDrawn = played.Match(found => found.Structure.Elements.Count > 0, () => false);
        zoom.IsFitted = true;
        Unread = IsDrawn ? [] : played.Match(
            found => (IReadOnlyList<Conversation.ICanvasViewModel>)[.. found.Document.Unread.Select((source, index) => new Conversation.CanvasViewModel(new Timeline.CanvasEntry($"unread-{index}", found.Document.Title, "text/vnd.mermaid", source, Canvas.Contracts.CanvasStatus.Completed)))],
            () => []);
        nodes.Clear();

        foreach (var element in played.Match(found => found.Structure.Elements.Concat(found.Removed).ToList(), () => []))
        {
            nodes.Add(new C4NodeViewModel(element));
        }

        sections.Clear();

        foreach (var shown in Steps)
        {
            sections.Add(new DocSectionViewModel(shown, GoTo));
        }

        foreach (var kept in played.Match(found => found.Document.Sections, () => []).Where(section => !Steps.Any(shown => shown.Anchor == section.Anchor || shown.Title == section.Title)))
        {
            sections.Add(new DocSectionViewModel(new PlayStep(StepKind.Base, kept.Anchor, kept.Title, kept.Text), GoTo));
        }
    }

    private void Render()
    {
        var scene = story.Match(found => C4Scene.Of(found, Version, step, hop), () => Scene.Empty);

        foreach (var node in nodes)
        {
            scene.Nodes.FirstOrDefault(shown => shown.Element.Alias == node.Key).ToOption().Match(shown => { node.Show(shown); return true; }, () => false);
        }

        Links = scene.Links;
        Frames = scene.Frames;
        Structure = Structure.SequenceEqual(scene.Structure) ? Structure : scene.Structure;
        Caption = scene.Caption;
        var steps = Steps;
        Position = DocsPhrases.Step(step, steps.Count, step >= 0 && step < steps.Count ? steps[step].Title : string.Empty);

        for (var index = 0; index < sections.Count; index++)
        {
            sections[index].Activate(index == step);
        }
    }

    private void Arm()
    {
        timer?.Dispose();
        var now = step >= 0 && step < Steps.Count ? Steps[step] : null;
        var pause = now is { Kind: StepKind.Flow } ? HopPause : StepPause;
        timer = time.CreateTimer(Ticked, null, pause / Speeds[speed], Timeout.InfiniteTimeSpan);
    }

    private void Ticked(object? state) => Ticking = dispatcher.InvokeAsync(Tick, CancellationToken.None).AsTask();

    private void Tick()
    {
        if (!IsPlaying)
        {
            return;
        }

        if (Advance())
        {
            Arm();
        }
        else
        {
            Pause();
        }
    }

    private void Pause()
    {
        IsPlaying = false;
        timer?.Dispose();
        timer = null;
    }
}
