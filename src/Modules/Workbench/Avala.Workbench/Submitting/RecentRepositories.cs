using Avala.Sdk;
using Avala.Workbench.Board;

namespace Avala.Workbench.Submitting;

internal sealed class RecentRepositories(JobBoard board, ScratchFolders scratch)
{
    public IEnumerable<string> Recent() =>
        board.Jobs.Values
            .OrderByDescending(job => job.Summary.Submitted)
            .Select(job => job.Summary.Repository)
            .Where(repository => !scratch.Holds(repository))
            .Distinct();

    public Option<(string Repository, bool Scratch)> Last() =>
        board.Jobs.Values
            .OrderByDescending(job => job.Summary.Submitted)
            .Select(job => (job.Summary.Repository, scratch.Holds(job.Summary.Repository)))
            .FirstOrDefault() is { Repository: { Length: > 0 } } last
            ? last
            : Option<(string, bool)>.None;
}
