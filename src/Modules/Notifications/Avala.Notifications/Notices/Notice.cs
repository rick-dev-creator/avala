using Avala.Jobs.Contracts;
using Avala.Notifications.Contracts;
using Avala.Sdk;

namespace Avala.Notifications.Notices;

internal sealed record Notice(NotificationKind Kind, string Subject, string Handle, DateTimeOffset At)
{
    public Severity Severity => Kind is NotificationKind.Decision or NotificationKind.Held or NotificationKind.Budget ? Severity.Urgent : Severity.Info;

    public Option<JobId> Job { get; init; }

    public string Repository { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public Option<string> Detail { get; init; }

    public int Count { get; init; } = 1;

    public Notice About(JobFacts facts) => this with { Repository = facts.Repository, Title = facts.Title };

    public Notice Joined(Notice other) => this with { Count = Count + other.Count, Detail = Detail.IsSome ? Detail : other.Detail };
}

internal sealed record JobFacts(string Repository, string Title)
{
    public const int LongestTitle = 60;

    public static JobFacts Of(string repository, string instruction) => new(Leaf(repository), Shortened(instruction));

    public static string Leaf(string repository) =>
        repository.Split((char[])['/', '\\'], StringSplitOptions.RemoveEmptyEntries) is [.., var leaf] ? leaf : repository;

    private static string Shortened(string instruction)
    {
        var line = instruction.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

        return line.Length <= LongestTitle ? line : $"{line[..(LongestTitle - 1)].TrimEnd()}…";
    }
}
