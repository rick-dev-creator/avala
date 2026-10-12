using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Preview.Navigation;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Preview.Pane;

internal interface IRunScriptViewModel
{
    bool IsShown { get; }

    bool IsRunning { get; }

    string Label { get; }

    string Problem { get; }

    bool HasProblem { get; }

    IAsyncRelayCommand ToggleCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class RunScriptViewModel(PreviewDesk desk, IMessenger messenger)
    : IRunScriptViewModel, IRegionAware<JobId>, IActivatable, IPresentation, IDisposable
{
    private Option<JobId> job;
    private Option<string> script;

    public event EventHandler<Presented>? Presented;

    public long Revision { get; private set; }

    public Task Following => desk.Feed.Following;

    public Task Finding { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial bool IsRunning { get; private set; }

    [ObservableProperty]
    public partial string Label { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string Problem { get; private set; } = string.Empty;

    public bool HasProblem => Problem.Length > 0;

    public void Activate()
    {
        desk.Feed.Start(Refresh);
        Refresh();
    }

    public void Deactivate() => desk.Feed.Stop();

    public void Dispose() => desk.Feed.Dispose();

    public void OnRegionContextChanged(Option<JobId> context)
    {
        job = context;
        script = Option<string>.None;
        Problem = string.Empty;
        Refresh();
        Finding = job.Match(FindAsync, () => Task.CompletedTask);
    }

    [RelayCommand(CanExecute = nameof(IsShown))]
    private Task ToggleAsync(CancellationToken cancellationToken) =>
        job.Match(current => SwitchAsync(current, cancellationToken), () => Task.CompletedTask);

    private async Task SwitchAsync(JobId current, CancellationToken cancellationToken)
    {
        var wasRunning = IsRunning;
        var outcome = wasRunning ? await desk.StopAsync(current, cancellationToken) : await desk.RunAsync(current, cancellationToken);
        Problem = outcome.Match(_ => string.Empty, PreviewPhrases.Problem);

        if (outcome.IsSuccess && !wasRunning)
        {
            messenger.Send(new PreviewRequested(current));
        }

        Refresh();
    }

    private async Task FindAsync(JobId wanted)
    {
        var found = await desk.ScriptAsync(wanted, CancellationToken.None);

        if (job == Option<JobId>.Some(wanted))
        {
            script = found;
            Refresh();
        }
    }

    private void Refresh()
    {
        IsRunning = job.Match(desk.IsRunning, () => false);
        var name = script.Match(found => found, () => string.Empty);
        IsShown = name.Length > 0 || IsRunning;
        Label = name.Length == 0 ? string.Empty : IsRunning ? PreviewPhrases.Stop(name) : PreviewPhrases.Run(name);
        Revision++;
        Presented?.Invoke(this, new Presented(Revision));
    }
}
