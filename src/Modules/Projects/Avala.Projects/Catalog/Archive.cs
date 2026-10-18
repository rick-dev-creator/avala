using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Domain;

namespace Avala.Projects.Catalog;

internal readonly record struct Shelved(JobId Job, JobStatus Status, Option<JobId> Parent);

internal sealed record JobsArchived(ImmutableArray<JobId> Jobs) : IDomainEvent;

internal sealed record JobRestored(JobId Job) : IDomainEvent;

internal static class Archive
{
    public static Result<JobsArchived, ProjectError> Of(IReadOnlyList<Shelved> jobs) =>
        jobs.Count == 0
            ? ProjectError.UnknownJob
            : jobs.Select(Refusal).FirstOrDefault(refusal => refusal.IsSome).Match(
                refused => Result<JobsArchived, ProjectError>.Failure(refused),
                () => new JobsArchived([.. jobs.Select(job => job.Job).Distinct()]));

    public static Result<JobRestored, ProjectError> Restore(JobId job, IReadOnlySet<JobId> archived) =>
        archived.Contains(job) ? new JobRestored(job) : ProjectError.NotArchived;

    public static bool Returns(JobStatus status) => status is JobStatus.Preparing or JobStatus.Running or JobStatus.Checking;

    private static Option<ProjectError> Refusal(Shelved job) =>
        job.Parent.IsSome ? ProjectError.FollowsItsParent
        : job.Status is JobStatus.Draft || Returns(job.Status) ? ProjectError.StillWorking
        : Option<ProjectError>.None;
}
