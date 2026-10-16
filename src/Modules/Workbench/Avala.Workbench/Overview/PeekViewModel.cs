using System.Collections.ObjectModel;
using Avala.Workbench.Board;
using Avala.Workbench.Conversation;
using Avala.Workbench.Presenting;

namespace Avala.Workbench.Overview;

internal interface IPeekViewModel
{
    IReadOnlyList<object> Latest { get; }

    IComposerViewModel Composer { get; }
}

internal sealed class PeekViewModel(ConversationViewModel conversation) : IPeekViewModel
{
    public const int Shown = 6;

    private readonly ObservableCollection<object> latest = [];

    public IReadOnlyList<object> Latest => latest;

    public IComposerViewModel Composer => conversation.Composer;

    public void Show(BoardJob job)
    {
        conversation.Show(job);
        latest.ShowInPlace(conversation.Entries.TakeLast(Shown), ReferenceEquals, entry => entry, (_, _) => { });

        while (latest.Count > Shown)
        {
            latest.RemoveAt(0);
        }
    }
}
