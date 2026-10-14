using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Workbench.Archiving;
using Avala.Workbench.Sidebar;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Archive;

internal interface IArchivedRowViewModel
{
    JobId Job { get; }

    string Title { get; }

    string Status { get; }

    string Ended { get; }

    IStatusDotViewModel Dot { get; }

    IRelayCommand OpenCommand { get; }

    IAsyncRelayCommand RestoreCommand { get; }
}

internal sealed record ArchivedGroup(string Name, string Count, IReadOnlyList<IArchivedRowViewModel> Rows);

internal sealed class ArchivedRowViewModel : IArchivedRowViewModel
{
    public ArchivedRowViewModel(ArchivedConversation conversation, DateTimeOffset now, Action<JobId> open, Func<IReadOnlyList<JobId>, CancellationToken, Task> restore)
    {
        Job = conversation.Lead.Job;
        Title = FactPhrases.Title(conversation.Instruction);
        Status = ArchivePhrases.Status(conversation.Lead.Status);
        Ended = ArchivePhrases.Ended(conversation.Ended, now);
        Dot = new StatusDotViewModel(FactPhrases.Dot(conversation.Lead));
        OpenCommand = new RelayCommand(() => open(Job));
        RestoreCommand = new AsyncRelayCommand(token => restore(conversation.Jobs, token));
    }

    public JobId Job { get; }

    public string Title { get; }

    public string Status { get; }

    public string Ended { get; }

    public IStatusDotViewModel Dot { get; }

    public IRelayCommand OpenCommand { get; }

    public IAsyncRelayCommand RestoreCommand { get; }
}
