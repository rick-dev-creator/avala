using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;

namespace Avala.Preview.Pane;

internal sealed record PreviewRequested(JobId Job);

internal sealed record PreviewPaneChanged(JobId Job, bool IsOpen);

internal static class PreviewPhrases
{
    public const string Running = "running";

    public const string Stopped = "stopped";

    public const string Starting = "starting";

    public const string Waiting = "Waiting for the server to listen on its port…";

    public const string Responsive = "Responsive";

    public const string FitsThePane = "fits the pane";

    public static string Size(int width, int height) => $"{width} × {height}";

    public static string Device(string name, string size) => $"{name} · {size}";

    public static string Chip(PreviewTarget target) => $"Running · {target.Origin}";

    public static string Lease(PreviewTarget target) => target.Leased ? string.Empty : "unleased";

    public static string Refusal(Uri address) =>
        address.IsAbsoluteUri && address.Host.Length > 0 ? $"Open {address.Host} in your browser?" : "Open this page in your browser?";

    public static string Notice(SurfaceEmbedding embedding) => embedding switch
    {
        SurfaceEmbedding.Window => "The page opened in a window of its own, since this desktop cannot show it inside Avala.",
        SurfaceEmbedding.None => "This computer has no web view Avala can show the page in. Open it in your browser instead.",
        _ => string.Empty,
    };

    public static string Run(string script) =>
        script.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? $"Open {script}" : $"Run {script}";

    public static string Stop(string script) => $"Stop {script}";

    public static string Problem(PreviewError error) => error switch
    {
        PreviewError.NoWorktree => "This job's worktree is gone.",
        PreviewError.NoScript => "Nothing to run: no dev or start script and no index.html.",
        PreviewError.NoLease => "No free ports are left for this worktree.",
        PreviewError.NotStarted => "The script could not start.",
        _ => "Nothing is running.",
    };
}
