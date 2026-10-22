using System.Globalization;
using Avala.Documentation.Contracts;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Conversation;

internal interface IDocsLineViewModel
{
    string File { get; }

    string Fact { get; }

    bool IsDeviation { get; }
}

[INotifyPropertyChanged]
internal sealed partial class DocsLineViewModel : IDocsLineViewModel, ITimelineItem
{
    public DocsLineViewModel(ITimelineEntry entry) => Update(entry);

    public bool IsShown => true;

    [ObservableProperty]
    public partial string File { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Fact { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsDeviation { get; private set; }

    public void Update(ITimelineEntry entry)
    {
        if (entry is DocsEntry docs)
        {
            File = Path.GetFileName(docs.Version.Path);
            Fact = DocsLinePhrases.Fact(docs.Feature, docs.Version.Kind);
            IsDeviation = docs.Version.Kind == VersionKind.Deviation;
        }
        else if (entry is UnwrittenEntry unwritten)
        {
            File = unwritten.Waiting ? "Version waiting" : "Version not written";
            Fact = unwritten.Waiting ? DocsLinePhrases.Waiting(unwritten.Feature, unwritten.Version) : DocsLinePhrases.Fact(unwritten.Feature, unwritten.Version);
            IsDeviation = false;
        }
    }
}

internal interface IChangeLineViewModel
{
    string Text { get; }

    string Fact { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ChangeLineViewModel : IChangeLineViewModel, ITimelineItem
{
    public ChangeLineViewModel(ChangeEntry entry) => Update(entry);

    public bool IsShown => true;

    [ObservableProperty]
    public partial string Text { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Fact { get; private set; } = string.Empty;

    public void Update(ITimelineEntry entry)
    {
        if (entry is ChangeEntry change)
        {
            Text = string.Create(CultureInfo.InvariantCulture, $"Change request #{change.Request.Number} recorded");
            Fact = DocsLinePhrases.Fact(change.Request);
        }
    }
}

internal static class DocsLinePhrases
{
    public static string Fact(string feature, VersionKind kind) => kind switch
    {
        VersionKind.Proposed => $"{feature} · proposed",
        VersionKind.Deviation => $"{feature} · deviation",
        _ => $"{feature} · as built",
    };

    public static string Fact(string feature, UnwrittenVersion unwritten) =>
        $"{Fact(feature, unwritten.Kind)} · {Why(unwritten)}";

    public static string Fact(ChangeRequest request)
    {
        var kind = request.Kind switch
        {
            RequestKind.Steer => "steer",
            RequestKind.SendBack => "sent back",
            _ => "message",
        };

        return request.Version.Match(version => string.Create(CultureInfo.InvariantCulture, $"{kind} → v{version}"), () => kind);
    }

    public static string Waiting(string feature, UnwrittenVersion waiting) =>
        $"{Fact(feature, waiting.Kind)} · its writer is {waiting.Hold.Match(FactPhrases.Held, () => "held")} and goes on when the window resets";

    public static string Why(UnwrittenVersion unwritten) =>
        unwritten.Hold.Match(
            reason => $"its writer was {FactPhrases.Held(reason)}",
            () => unwritten.Failure switch
            {
                VersionFailure.WriterRefused => "its writer could not start",
                VersionFailure.NoDraft => "its writer left no draft",
                VersionFailure.Unwritable => "it could not be saved",
                _ => "its writer stopped",
            });
}

internal sealed record DesignDocsLineViewModel(string File, string Fact, bool IsDeviation) : IDocsLineViewModel
{
    public DesignDocsLineViewModel()
        : this("v2-deviation.md", "Refund webhooks · deviation", true)
    {
    }
}

internal sealed record DesignChangeLineViewModel(string Text, string Fact) : IChangeLineViewModel
{
    public DesignChangeLineViewModel()
        : this("Change request #1 recorded", "steer → v2")
    {
    }
}
