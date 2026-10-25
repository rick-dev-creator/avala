using Avala.Jobs.Contracts;
using Avala.Sdk.Events;

namespace Avala.Preview.Contracts;

public enum PreviewError
{
    NoWorktree,
    NoScript,
    NoLease,
    NotStarted,
    NotRunning,
    Outside,
}

public enum PreviewSource
{
    Server,
    File,
}

public sealed record PreviewTarget(JobId Job, Uri Address, PreviewSource Source)
{
    public bool Leased { get; init; }

    public string Origin => $"{Address.Host}:{Address.Port}";
}

public sealed record PreviewRun(JobId Job, string Script);

public sealed record PreviewAvailable(PreviewTarget Target) : IIntegrationEvent;

public sealed record PreviewGone(PreviewTarget Target) : IIntegrationEvent;

public sealed record PreviewRunChanged(JobId Job, bool Running) : IIntegrationEvent;

public sealed record WorktreeFileRequested(JobId Job, string Path)
{
    public bool IsShown { get; private set; }

    public void Show() => IsShown = true;
}
