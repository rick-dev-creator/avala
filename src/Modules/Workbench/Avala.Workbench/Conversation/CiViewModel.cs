using System.Windows.Input;
using Avala.Forges.Contracts;
using Avala.Workbench.Linking;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal interface ICiViewModel
{
    string Headline { get; }

    bool IsFailed { get; }

    IReadOnlyList<ICiCheck> Failing { get; }

    string Note { get; }

    bool CanAsk { get; }

    string Request { get; }

    ICommand AskCommand { get; }
}

internal interface ICiCheck
{
    string Name { get; }

    string Summary { get; }

    bool HasLog { get; }

    string LogLink { get; }

    IAsyncRelayCommand OpenLogCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class CiViewModel : ICiViewModel, ITimelineItem
{
    private static readonly ICommand Unbound = new RelayCommand(() => { }, () => false);

    private readonly Links links;
    private bool accepts;
    private bool woken;

    public CiViewModel(CiEntry entry, Links links)
    {
        this.links = links;
        Update(entry);
    }

    public bool IsShown => true;

    [ObservableProperty]
    public partial string Headline { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsFailed { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<ICiCheck> Failing { get; private set; } = [];

    [ObservableProperty]
    public partial string Note { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanAsk { get; private set; }

    [ObservableProperty]
    public partial string Request { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial ICommand AskCommand { get; private set; } = Unbound;

    public void Update(ITimelineEntry entry)
    {
        if (entry is not CiEntry ci)
        {
            return;
        }

        var failing = ci.Checks.Where(check => check.Status == CheckStatus.Failed).ToList();
        IsFailed = failing.Count > 0;
        woken = ci.Woken;
        Headline = ConversationPhrases.Ci(ci);
        Failing = [.. failing.Select(check => new CiCheck(check, links))];
        Request = IsFailed ? ConversationPhrases.FixChecks(ci) : string.Empty;
        Note = IsFailed && woken ? ConversationPhrases.WokenForChecks : string.Empty;
        CanAsk = IsFailed && !woken && accepts;
    }

    public void Mark(bool acceptsMessages, ICommand ask)
    {
        accepts = acceptsMessages;
        CanAsk = IsFailed && !woken && accepts;
        AskCommand = ask;
    }
}

internal sealed partial class CiCheck(CheckRun check, Links links) : ICiCheck
{
    public string Name => check.Name;

    public string Summary => check.Summary;

    public bool HasLog => check.Link.IsSome;

    public string LogLink => check.Link.Match(found => found.AbsoluteUri, () => string.Empty);

    [RelayCommand(CanExecute = nameof(HasLog))]
    private async Task OpenLogAsync(CancellationToken cancellationToken)
    {
        foreach (var link in check.Link.Match<Uri[]>(found => [found], () => []))
        {
            _ = await links.OpenAsync(link.ToString(), cancellationToken);
        }
    }
}
