using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Testing;
using Avala.Workbench.Archive;
using Avala.Workbench.Archiving;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Archive;

internal sealed class Archives : IDisposable
{
    public const string Ledger = "/home/rick/code/ledger";

    public const string Website = "/home/rick/code/website";

    public const string Chat = "/data/scratch/2026-10-08-sketch-a-parser";

    private readonly FakeCatalog catalog = new();
    private ImmutableDictionary<JobId, BoardJob> jobs = ImmutableDictionary<JobId, BoardJob>.Empty;

    public Archives()
    {
        Bench.ProjectBook.Add("ledger", Ledger);
        Bench.ProjectBook.Add("website", Website);
        Bench.Board.Publish(Bench.ProjectBook.Current);
    }

    public Bench Bench { get; } = new();

    public TestUiDispatcher Ui => Bench.Ui;

    public ArchivedReader Reader => new(Bench.Board, Bench.ScratchFolders, Bench.Time);

    public LiveFeed Feed() => new(new Pulse(Bench.Board), Ui, Microsoft.Extensions.Logging.Abstractions.NullLogger<LiveFeed>.Instance);

    public BoardJob Job(string instruction, JobStatus status, string repository, TimeSpan endedAgo)
    {
        var summary = catalog.Add(instruction, status).Summary;
        var job = new BoardJob(summary with { Repository = repository, Ended = Bench.Time.GetUtcNow() - endedAgo }, Transcript.Empty);
        jobs = jobs.SetItem(job.Job, job);
        Bench.Board.Publish(jobs);

        return job;
    }

    public void Archive(params BoardJob[] archived)
    {
        Bench.ProjectBook.Current = Bench.ProjectBook.Current with { Archived = Bench.ProjectBook.Current.Archived.Union(archived.Select(job => job.Job)) };
        Bench.Board.Publish(Bench.ProjectBook.Current);
    }

    public ArchivedViewModel Page() => new(Reader, Feed(), Bench.Shelf, Bench.Focus);

    public void Dispose() => Bench.Dispose();
}
