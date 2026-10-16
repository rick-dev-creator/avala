using Avala.Jobs.Contracts;
using Avala.Workbench.Conversation;
using Avala.Workbench.Navigation;

namespace Avala.Workbench.Overview;

internal sealed class RowActions(JobFocus focus, Conversations conversations)
{
    public void Open(JobId job) => focus.Select(job);

    public void Peek(DelegationNodeViewModel row)
    {
        if (!row.IsPeeking)
        {
            row.Peek = null;

            return;
        }

        var peek = row.Peek as PeekViewModel ?? new PeekViewModel(conversations.Open(row.Job));
        row.Seen.Match(
            job =>
            {
                peek.Show(job);

                return true;
            },
            () => false);
        row.Peek = peek;
    }
}
