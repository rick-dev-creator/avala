using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Answering;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Permissions.Governance;

internal sealed class SessionTerms(IPolicyFiles files, IEnumerable<IJobTerms> plugins, IRealPaths links, WritablePlaces places)
{
    public SessionTerms(IPolicyFiles files, IEnumerable<IJobTerms> plugins, IRealPaths links)
        : this(files, plugins, links, new WritablePlaces(Option<AvalaPaths>.None, []))
    {
    }

    public SessionTerms(IPolicyFiles files, IEnumerable<IJobTerms> plugins, IRealPaths links, AvalaPaths paths)
        : this(files, plugins, links, new WritablePlaces(Option<AvalaPaths>.Some(paths), SystemTemporaryFolders.All))
    {
    }

    public IPolicyFiles Files => files;

    public IReadOnlyList<string> WritableOf(string workingDirectory) =>
        [
            .. places.Data.Match<string[]>(known => [ScratchFolders.Of(known, workingDirectory)], () => []),
            .. places.Temporary.Where(folder => !Holds(folder, workingDirectory) && places.Data.Match(known => !Holds(folder, known.Data), () => true)),
        ];

    private bool Holds(string folder, string path) =>
        Contains(Path.GetFullPath(folder), Path.GetFullPath(path)) || Contains(Real(folder), Real(path));

    private string Real(string path) => links.Resolve(Path.GetFullPath(path)).Match(real => real, () => Path.GetFullPath(path));

    private static bool Contains(string folder, string path)
    {
        var relative = Path.GetRelativePath(folder, path);

        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    public async Task<JobTerms> OfAsync(JobId job, CancellationToken cancellationToken)
    {
        var terms = JobTerms.Full;

        foreach (var plugin in plugins)
        {
            var declared = await plugin.OfAsync(job, cancellationToken);
            terms = new JobTerms(terms.ReadOnly || declared.ReadOnly, terms.AsksParent.IsSome ? terms.AsksParent : declared.AsksParent);
        }

        return terms;
    }
}
