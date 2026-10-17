using Avala.Documentation.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Timeline;

internal static class DocsLines
{
    public static Transcript WithDocs(this Transcript transcript, Option<JobDocumentation> documented) =>
        documented.Match(
            found => found.Requests.Aggregate(
                found.Versions.Aggregate(transcript, (shown, version) => shown.Kept(new DocsEntry(EntryKeys.Docs(version.Number), found.Feature.Title, version))),
                (shown, request) => shown.Kept(new ChangeEntry(EntryKeys.Change(request.Number), request))),
            () => transcript);
}
