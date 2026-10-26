using System.Text.Json;
using System.Text.Json.Nodes;
using Avala.Resources.Contracts;
using Avala.Sdk;

namespace Avala.Resources.Settings;

internal static class ResourceSettingsParser
{
    private const string Sample = "sampleSeconds";
    private const string Disk = "diskSeconds";
    private const string Orphans = "orphans";
    private const string Ports = "ports";
    public const string Worktrees = "worktrees";
    private const string First = "first";
    private const string Last = "last";
    private const string PerWorktree = "perWorktree";
    private const string KeptDiscarded = "keepDiscardedHours";
    private const string KeptFailed = "keepFailedHours";
    private const string KeptApproved = "keepApprovedHours";
    private const string Reconcile = "reconcile";

    public const string FreeArchived = "freeArchivedHours";
    public const string FreeDelivered = "freeDeliveredHours";
    public const string FreePullRequest = "freePullRequestHours";
    public const string FreeUndelivered = "freeUndeliveredHours";
    public const string FreeDiscarded = "freeDiscardedHours";
    public const string FreeFailed = "freeFailedHours";
    public const string DeleteDiscarded = "deleteDiscardedHours";
    public const string DeleteFailed = "deleteFailedHours";
    public const string FreeIdle = "freeIdleHours";
    public const string SpaceBudget = "spaceBudgetGigabytes";
    public const string DiskFloor = "diskFloorPercent";
    private const long Gigabyte = 1L << 30;
    private const double LargestBudget = 1_000_000;

    private static readonly string[] WorktreeFields =
    [
        FreeArchived, FreeDelivered, FreePullRequest, FreeUndelivered, FreeDiscarded, FreeFailed,
        DeleteDiscarded, DeleteFailed, FreeIdle, SpaceBudget, DiskFloor, KeptDiscarded, KeptFailed, KeptApproved, Reconcile,
    ];
    private const double LongestInterval = 86_400;
    private const double LongestRetention = 87_600;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 3, AllowDuplicateProperties = false };

    public static ResourceSettings Defaults { get; } = new(
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMinutes(1),
        OrphanPolicy.Kill,
        new PortRange(24_000, 24_999, 10),
        new WorktreePolicy(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromDays(1), TimeSpan.Zero, TimeSpan.Zero)
        {
            DeleteDiscarded = TimeSpan.FromDays(7),
            DeleteFailed = TimeSpan.FromDays(30),
            Idle = TimeSpan.FromDays(3),
            SpaceBudget = 20 * Gigabyte,
            DiskFloor = 0.1,
        },
        ReconcilePolicy.Clean);

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
            return Defaults with { Sampling = sampling, DiskSampling = disk, Orphans = orphans, Ports = ports, Worktrees = worktrees.Policy, Reconcile = worktrees.Reconcile };
        }

        return error;
    }

    private static Result<bool, ResourceError> Fields(JsonElement element, string[] fields) =>
        element.ValueKind != JsonValueKind.Object ? ResourceError.Malformed
        : element.EnumerateObject().Any(property => !fields.Contains(property.Name, StringComparer.Ordinal)) ? ResourceError.UnknownField
        : Result<bool, ResourceError>.Success(true);

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

    private static Result<PortRange, ResourceError> PortsOf(JsonElement root) =>
        root.TryGetProperty(Ports, out var ports) ? Range(ports) : Defaults.Ports;

    private static Result<PortRange, ResourceError> Range(JsonElement ports)
    {
        if (!Fields(ports, [First, Last, PerWorktree]).TryGetValue(out _, out var error)
            || !Whole(ports, First, Defaults.Ports.First).TryGetValue(out var first, out error)
            || !Whole(ports, Last, Defaults.Ports.Last).TryGetValue(out var last, out error)
            || !Whole(ports, PerWorktree, Defaults.Ports.PerWorktree).TryGetValue(out var each, out error))
        {
            return error;
        }

        return Usable(first, last, each) ? new PortRange(first, last, each) : ResourceError.InvalidPorts;
    }

    private static bool Usable(int first, int last, int each) =>
        first >= 1024 && last <= 65_535 && first <= last && each >= 1 && each <= last - first + 1;

    private static Result<int, ResourceError> Whole(JsonElement element, string name, int fallback) =>
        !element.TryGetProperty(name, out var value) ? fallback
        : value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var whole) ? whole
        : ResourceError.Malformed;

    private static Result<(WorktreePolicy Policy, ReconcilePolicy Reconcile), ResourceError> WorktreesOf(JsonElement root)
    {
        if (!root.TryGetProperty(Worktrees, out var worktrees))
        {
            return (Defaults.Worktrees, Defaults.Reconcile);
        }

        if (!Fields(worktrees, WorktreeFields).TryGetValue(out _, out var error)
            || !EarlierRetentionOf(worktrees).TryGetValue(out var earlier, out error)
            || !FreeingOf(worktrees, earlier).TryGetValue(out var freeing, out error)
            || !DeletingOf(worktrees, freeing, earlier).TryGetValue(out var deleting, out error)
            || !SpaceOf(worktrees, deleting).TryGetValue(out var policy, out error)
            || !Choice(worktrees, Reconcile, Defaults.Reconcile).TryGetValue(out var reconcile, out error))
        {
            return error;
        }

        return (policy, reconcile);
    }

    private static Result<EarlierRetention, ResourceError> EarlierRetentionOf(JsonElement worktrees)
    {
        var defaults = Defaults.Worktrees;

        if (!Hours(worktrees, KeptDiscarded, defaults.DeleteDiscarded).TryGetValue(out var discarded, out var error)
            || !Hours(worktrees, KeptFailed, defaults.DeleteFailed).TryGetValue(out var failed, out error)
            || !Hours(worktrees, KeptApproved, defaults.Undelivered).TryGetValue(out var approved, out error))
        {
            return error;
        }

        return new EarlierRetention(discarded, failed, approved);
    }

    private static Result<WorktreePolicy, ResourceError> FreeingOf(JsonElement worktrees, EarlierRetention earlier)
    {
        var defaults = Defaults.Worktrees;

        if (!Hours(worktrees, FreeArchived, defaults.Archived).TryGetValue(out var archived, out var error)
            || !Hours(worktrees, FreeDelivered, defaults.Delivered).TryGetValue(out var delivered, out error)
            || !Hours(worktrees, FreePullRequest, defaults.PullRequest).TryGetValue(out var pullRequest, out error)
            || !Hours(worktrees, FreeUndelivered, earlier.Approved).TryGetValue(out var undelivered, out error)
            || !Hours(worktrees, FreeDiscarded, defaults.Discarded).TryGetValue(out var discarded, out error)
            || !Hours(worktrees, FreeFailed, defaults.Failed).TryGetValue(out var failed, out error))
        {
            return error;
        }

        return new WorktreePolicy(archived, delivered, pullRequest, undelivered, discarded, failed);
    }

    private static Result<WorktreePolicy, ResourceError> DeletingOf(JsonElement worktrees, WorktreePolicy policy, EarlierRetention earlier)
    {
        if (!Hours(worktrees, DeleteDiscarded, earlier.Discarded).TryGetValue(out var discarded, out var error)
            || !Hours(worktrees, DeleteFailed, earlier.Failed).TryGetValue(out var failed, out error)
            || !Hours(worktrees, FreeIdle, Defaults.Worktrees.Idle).TryGetValue(out var idle, out error))
        {
            return error;
        }

        return policy with { DeleteDiscarded = discarded, DeleteFailed = failed, Idle = idle };
    }

    private static Result<WorktreePolicy, ResourceError> SpaceOf(JsonElement worktrees, WorktreePolicy policy)
    {
        var defaults = Defaults.Worktrees;

        if (!Amount(worktrees, SpaceBudget, defaults.SpaceBudget.Map(bytes => (double)bytes / Gigabyte), LargestBudget).TryGetValue(out var budget, out var error)
            || !Amount(worktrees, DiskFloor, defaults.DiskFloor.Map(share => share * 100), 90).TryGetValue(out var floor, out error))
        {
            return error;
        }

        return policy with { SpaceBudget = budget.Map(gigabytes => (long)(gigabytes * Gigabyte)), DiskFloor = floor.Map(percent => percent / 100) };
    }

    public static JsonObject Node(WorktreePolicy policy, ReconcilePolicy reconcile) => new()
    {
        [FreeArchived] = HoursNode(policy.Archived),
        [FreeDelivered] = HoursNode(policy.Delivered),
        [FreePullRequest] = HoursNode(policy.PullRequest),
        [FreeUndelivered] = HoursNode(policy.Undelivered),
        [FreeDiscarded] = HoursNode(policy.Discarded),
        [FreeFailed] = HoursNode(policy.Failed),
        [DeleteDiscarded] = HoursNode(policy.DeleteDiscarded),
        [DeleteFailed] = HoursNode(policy.DeleteFailed),
        [FreeIdle] = HoursNode(policy.Idle),
        [SpaceBudget] = policy.SpaceBudget.Match<JsonValue?>(bytes => JsonValue.Create(Math.Round((double)bytes / Gigabyte, 3)), () => null),
        [DiskFloor] = policy.DiskFloor.Match<JsonValue?>(share => JsonValue.Create(Math.Round(share * 100, 3)), () => null),
        [Reconcile] = reconcile.ToString().ToLowerInvariant(),
    };

    private static JsonValue? HoursNode(Option<TimeSpan> span) => span.Match<JsonValue?>(kept => JsonValue.Create(kept.TotalHours), () => null);

    private static Result<Option<double>, ResourceError> Amount(JsonElement element, string name, Option<double> fallback, double largest) =>
        !element.TryGetProperty(name, out var value) ? fallback
        : value.ValueKind == JsonValueKind.Null ? Option<double>.None
        : value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var amount) ? ResourceError.Malformed
        : amount > 0 && amount <= largest ? Option<double>.Some(amount)
        : ResourceError.InvalidRetention;

    private static Result<Option<TimeSpan>, ResourceError> Hours(JsonElement element, string name, Option<TimeSpan> fallback) =>
        !element.TryGetProperty(name, out var value) ? fallback
        : value.ValueKind == JsonValueKind.Null ? Option<TimeSpan>.None
        : value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var hours) ? ResourceError.Malformed
        : hours >= 0 && hours <= LongestRetention ? Option<TimeSpan>.Some(TimeSpan.FromHours(hours))
        : ResourceError.InvalidRetention;

    private sealed record EarlierRetention(Option<TimeSpan> Discarded, Option<TimeSpan> Failed, Option<TimeSpan> Approved);
}
