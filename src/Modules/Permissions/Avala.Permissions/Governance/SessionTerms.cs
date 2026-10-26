using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Permissions.Governance;

internal sealed class SessionTerms(IPolicyFiles files, IEnumerable<IJobTerms> plugins, Option<AvalaPaths> paths, IReadOnlyList<string> temporary)
{
    public SessionTerms(IPolicyFiles files, IEnumerable<IJobTerms> plugins)
        : this(files, plugins, Option<AvalaPaths>.None, [])
    {
    }

    public SessionTerms(IPolicyFiles files, IEnumerable<IJobTerms> plugins, AvalaPaths paths)
        : this(files, plugins, Option<AvalaPaths>.Some(paths), SystemTemporaryFolders.All)
    {
    }

    public IPolicyFiles Files => files;

    public IReadOnlyList<string> WritableOf(string workingDirectory) =>
        [
            .. paths.Match<string[]>(known => [ScratchFolders.Of(known, workingDirectory)], () => []),
            .. temporary.Where(folder => !Holds(folder, workingDirectory) && paths.Match(known => !Holds(folder, known.Data), () => true)),
        ];

    private static bool Holds(string folder, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(folder), Path.GetFullPath(path));

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
