using Avala.Documentation.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Timeline;

internal static class DocsLines
{
    public static Transcript WithDocs(this Transcript transcript, Option<JobDocumentation> documented) =>
        documented.Match(
            found => found.Versions
                .Select(version => (version.Attempt, version.At, Entry: (ITimelineEntry)new DocsEntry(EntryKeys.Docs(version.Number), found.Feature.Title, version)))
                .Concat(found.Requests.Select(request => (request.Attempt, request.At, Entry: (ITimelineEntry)new ChangeEntry(EntryKeys.Change(request.Number), request))))
                .OrderBy(line => line.Attempt)
                .ThenBy(line => line.At)
                .Aggregate(transcript, (shown, line) => shown.KeptIn(line.Attempt, line.Entry)),
            () => transcript);
}
