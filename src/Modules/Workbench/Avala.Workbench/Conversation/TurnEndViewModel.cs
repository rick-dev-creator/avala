using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal interface ITurnEndViewModel
{
    TurnOutcome Outcome { get; }

    TimeSpan Duration { get; }

    string Summary { get; }

    long Tokens { get; }

    string Cost { get; }

    bool HasUsage { get; }

    string Breakdown { get; }

    bool HasCheckpoint { get; }

    string Checkpoint { get; }

    IReadOnlyList<string> Files { get; }

    int Attempt { get; }

    bool CanRestore { get; }

    bool IsConfirming { get; }

    IRelayCommand AskRestoreCommand { get; }

    IAsyncRelayCommand<int> RestoreCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class TurnEndViewModel(TurnEndEntry entry) : ITurnEndViewModel, ITimelineItem
{
    private static readonly IAsyncRelayCommand<int> Unbound = new AsyncRelayCommand<int>(_ => Task.CompletedTask, _ => false);

    public TurnOutcome Outcome { get; } = entry.Outcome;

    public TimeSpan Duration { get; } = entry.Duration;

    public string Summary { get; } = ConversationPhrases.Turn(entry.Outcome, entry.Duration);

    public long Tokens { get; } = entry.Tokens.Input + entry.Tokens.Output + entry.Tokens.CacheRead + entry.Tokens.CacheWrite + entry.Tokens.Reasoning;

    public string Cost { get; } = entry.Costs.Count == 0 ? string.Empty : Presenting.Amounts.Costs(entry.Costs);

    public bool HasUsage { get; } = ConversationPhrases.Reported(entry.Tokens) || entry.Costs.Count > 0;

    public string Breakdown { get; } = ConversationPhrases.Breakdown(entry.Tokens, entry.Model, entry.Costs);

    public bool IsShown => true;

    [ObservableProperty]
    public partial bool HasCheckpoint { get; private set; }

    [ObservableProperty]
    public partial string Checkpoint { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> Files { get; private set; } = [];

    [ObservableProperty]
    public partial int Attempt { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskRestoreCommand))]
    public partial bool CanRestore { get; private set; }

    [ObservableProperty]
    public partial bool IsConfirming { get; private set; }

    [ObservableProperty]
    public partial IAsyncRelayCommand<int> RestoreCommand { get; private set; } = Unbound;

    public void Update(ITimelineEntry entry)
    {
    }

    public void Mark(Option<JobCheckpoint> checkpoint, bool restorable, IAsyncRelayCommand<int> restore)
    {
        HasCheckpoint = checkpoint.IsSome;
        Attempt = checkpoint.Match(found => found.Attempt, () => 0);
        Checkpoint = checkpoint.Match(found => ConversationPhrases.Checkpoint(found.Files.Count), () => string.Empty);
        var files = checkpoint.Match(found => found.Files.Select(ConversationPhrases.Change).ToList(), () => []);
        Files = Files.SequenceEqual(files) ? Files : files;
        CanRestore = HasCheckpoint && restorable;
        IsConfirming = IsConfirming && CanRestore;
        RestoreCommand = restore;
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private void AskRestore() => IsConfirming = !IsConfirming;
}
