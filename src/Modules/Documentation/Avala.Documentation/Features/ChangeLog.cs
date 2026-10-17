using System.Globalization;
using System.Text;
using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;

namespace Avala.Documentation.Features;

internal static class ChangeLog
{
    public const string File = "changes.md";

    private const string Heading = "# Change requests\n\nWhat people asked while the feature was built, and the version each request produced.\n";

    public static string Merge(string existing, JobId job, string instruction, IReadOnlyList<ChangeRequest> requests, IReadOnlyList<VersionFile> versions)
    {
        var start = Start(job);
        var end = $"<!-- /avala:job {job.Value} -->";
        var kept = existing.Length == 0 ? Heading : existing;
        var from = kept.IndexOf(start, StringComparison.Ordinal);

        if (from >= 0)
        {
            var to = kept.IndexOf(end, from, StringComparison.Ordinal);
            kept = kept[..from].TrimEnd('\n') + "\n" + (to < 0 ? string.Empty : kept[(to + end.Length)..].TrimStart('\n'));
        }

        return requests.Count == 0 ? kept : kept.TrimEnd('\n') + "\n\n" + Section(job, instruction, requests, versions) + end + "\n";
    }

    private static string Start(JobId job) => $"<!-- avala:job {job.Value} -->";

    private static string Section(JobId job, string instruction, IReadOnlyList<ChangeRequest> requests, IReadOnlyList<VersionFile> versions)
    {
        var section = new StringBuilder(Start(job)).Append('\n');
        section.Append(CultureInfo.InvariantCulture, $"## {FirstLine(instruction)}\n\n");

        foreach (var request in requests)
        {
            section.Append(CultureInfo.InvariantCulture, $"### Change request {request.Number} · {Kind(request.Kind)} · attempt {request.Attempt}\n\n");
            section.Append(string.Concat(request.Text.Trim().Split('\n').Select(line => $"> {line}\n")));
            section.Append(CultureInfo.InvariantCulture, $"\nAsked {request.At.ToUniversalTime():yyyy-MM-dd HH:mm} UTC. ");
            section.Append(request.Version.Match(
                number => versions.FirstOrDefault(version => version.Number == number) is { } produced ? $"Produced [v{number}]({produced.File}).\n\n" : $"Produced v{number}.\n\n",
                () => "No version yet.\n\n"));
        }

        return section.ToString();
    }

    private static string Kind(RequestKind kind) => kind switch
    {
        RequestKind.Steer => "steer",
        RequestKind.SendBack => "sent back in review",
        _ => "message",
    };

    private static string FirstLine(string instruction) =>
        FeatureNames.Untagged(instruction).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "A job";
}
