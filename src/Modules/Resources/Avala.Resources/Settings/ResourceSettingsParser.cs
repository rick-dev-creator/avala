using System.Text.Json;
using Avala.Resources.Contracts;
using Avala.Sdk;

namespace Avala.Resources.Settings;

internal static class ResourceSettingsParser
{
    private const string Sample = "sampleSeconds";
    private const string Disk = "diskSeconds";
    private const string Orphans = "orphans";
    private const string Ports = "ports";
    private const string Worktrees = "worktrees";
    private const string First = "first";
    private const string Last = "last";
    private const string PerWorktree = "perWorktree";
    private const string Discarded = "keepDiscardedHours";
    private const string Failed = "keepFailedHours";
    private const string Approved = "keepApprovedHours";
    private const string Reconcile = "reconcile";
    private const double LongestInterval = 86_400;
    private const double LongestRetention = 87_600;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 3, AllowDuplicateProperties = false };

    public static ResourceSettings Defaults { get; } = new(
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMinutes(1),
        OrphanPolicy.Kill,
        new PortRange(24_000, 24_999, 10),
        new WorktreeRetention(TimeSpan.Zero, TimeSpan.FromDays(7), Option<TimeSpan>.None),
        ReconcilePolicy.Report);

    public static Result<ResourceSettings, ResourceError> Parse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, Options);

            return Settings(document.RootElement);
        }
        catch (JsonException)
        {
            return ResourceError.Malformed;
        }
    }

    private static Result<ResourceSettings, ResourceError> Settings(JsonElement root)
    {
        if (Fields(root, [Sample, Disk, Orphans, Ports, Worktrees]).TryGetValue(out _, out var error)
            && Interval(root, Sample, Defaults.Sampling).TryGetValue(out var sampling, out error)
            && Interval(root, Disk, Defaults.DiskSampling).TryGetValue(out var disk, out error)
            && Choice(root, Orphans, Defaults.Orphans).TryGetValue(out var orphans, out error)
            && PortsOf(root).TryGetValue(out var ports, out error)
            && WorktreesOf(root).TryGetValue(out var worktrees, out error))
        {
            return Defaults with { Sampling = sampling, DiskSampling = disk, Orphans = orphans, Ports = ports, Retention = worktrees.Retention, Reconcile = worktrees.Reconcile };
        }

        return error;
    }

    private static Result<bool, ResourceError> Fields(JsonElement element, string[] fields) =>
        element.ValueKind != JsonValueKind.Object ? ResourceError.Malformed
        : element.EnumerateObject().Any(property => !fields.Contains(property.Name, StringComparer.Ordinal)) ? ResourceError.UnknownField
        : true;

    private static Result<TimeSpan, ResourceError> Interval(JsonElement root, string name, TimeSpan fallback) =>
        !root.TryGetProperty(name, out var value) ? fallback
        : value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var seconds) ? ResourceError.Malformed
        : seconds >= 0.1 && seconds <= LongestInterval ? TimeSpan.FromSeconds(seconds)
        : ResourceError.InvalidInterval;

    private static Result<T, ResourceError> Choice<T>(JsonElement root, string name, T fallback)
        where T : struct, Enum =>
        !root.TryGetProperty(name, out var value) ? fallback
        : value.ValueKind != JsonValueKind.String ? ResourceError.Malformed
        : Enum.TryParse<T>(value.GetString(), ignoreCase: true, out var chosen) && Enum.IsDefined(chosen) && !char.IsDigit(value.GetString()![0]) ? chosen
        : ResourceError.UnknownPolicy;

    private static Result<PortRange, ResourceError> PortsOf(JsonElement root)
    {
        if (!root.TryGetProperty(Ports, out var ports))
        {
            return Defaults.Ports;
        }

        if (!Fields(ports, [First, Last, PerWorktree]).TryGetValue(out _, out var error)
            || !Whole(ports, First, Defaults.Ports.First).TryGetValue(out var first, out error)
            || !Whole(ports, Last, Defaults.Ports.Last).TryGetValue(out var last, out error)
            || !Whole(ports, PerWorktree, Defaults.Ports.PerWorktree).TryGetValue(out var each, out error))
        {
            return error;
        }

        return first >= 1024 && last <= 65_535 && first <= last && each >= 1 && each <= last - first + 1
            ? new PortRange(first, last, each)
            : ResourceError.InvalidPorts;
    }

    private static Result<int, ResourceError> Whole(JsonElement element, string name, int fallback) =>
        !element.TryGetProperty(name, out var value) ? fallback
        : value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var whole) ? whole
        : ResourceError.Malformed;

    private static Result<(WorktreeRetention Retention, ReconcilePolicy Reconcile), ResourceError> WorktreesOf(JsonElement root)
    {
        if (!root.TryGetProperty(Worktrees, out var worktrees))
        {
            return (Defaults.Retention, Defaults.Reconcile);
        }

        if (!Fields(worktrees, [Discarded, Failed, Approved, Reconcile]).TryGetValue(out _, out var error)
            || !Hours(worktrees, Discarded, Defaults.Retention.Discarded).TryGetValue(out var discarded, out error)
            || !Hours(worktrees, Failed, Defaults.Retention.Failed).TryGetValue(out var failed, out error)
            || !Hours(worktrees, Approved, Defaults.Retention.Approved).TryGetValue(out var approved, out error)
            || !Choice(worktrees, Reconcile, Defaults.Reconcile).TryGetValue(out var reconcile, out error))
        {
            return error;
        }

        return (new WorktreeRetention(discarded, failed, approved), reconcile);
    }

    private static Result<Option<TimeSpan>, ResourceError> Hours(JsonElement element, string name, Option<TimeSpan> fallback) =>
        !element.TryGetProperty(name, out var value) ? fallback
        : value.ValueKind == JsonValueKind.Null ? Option<TimeSpan>.None
        : value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var hours) ? ResourceError.Malformed
        : hours >= 0 && hours <= LongestRetention ? Option<TimeSpan>.Some(TimeSpan.FromHours(hours))
        : ResourceError.InvalidRetention;
}
