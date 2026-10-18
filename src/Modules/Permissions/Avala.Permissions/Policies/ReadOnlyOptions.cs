using System.Collections.Frozen;

namespace Avala.Permissions.Policies;

internal sealed record OptionSet(string Flags, string Valued, FrozenSet<string> Long, FrozenSet<string> LongValued)
{
    public string Attached { get; init; } = string.Empty;

    public bool Digits { get; init; }

    public bool TakesPattern { get; init; }

    public bool Paths { get; init; } = true;

    public bool Free { get; init; }

    public int Most { get; init; } = int.MaxValue;

    public FrozenSet<string> PatternOptions { get; init; } = FrozenSet<string>.Empty;

    public static OptionSet Of(string flags, string valued, string longs, string longValued) =>
        new(flags, valued, Words(longs), Words(longValued));

    public static FrozenSet<string> Words(string words) =>
        words.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToFrozenSet(StringComparer.Ordinal);
}

internal static class ReadOnlyOptions
{
    private const string Help = "help version";

    private const string GitDiffing =
        "stat shortstat numstat name-only name-status patch no-patch raw summary minimal patience histogram no-renames find-renames find-copies color no-color word-diff "
        + "ignore-all-space ignore-space-change ignore-blank-lines ignore-space-at-eol cached staged exit-code quiet check relative no-relative full-index binary abbrev "
        + "text ignore-submodules dirstat compact-summary no-textconv src-prefix dst-prefix no-prefix unified pretty dirstat-by-file";

    private const string GitLogging =
        "oneline graph all branches tags remotes decorate no-decorate abbrev-commit no-abbrev-commit reverse first-parent merges no-merges follow topo-order date-order "
        + "left-right cherry-pick cherry-mark boundary no-walk source full-history simplify-merges ancestry-path regexp-ignore-case all-match invert-grep fixed-strings "
        + "basic-regexp extended-regexp perl-regexp children parents log-size walk-reflogs mailmap no-mailmap";

    private const string GitLogValued =
        "max-count skip since until after before author committer grep format date diff-filter inter-hunk-context stat-width encoding";

    public static FrozenDictionary<string, OptionSet> Programs { get; } = new Dictionary<string, OptionSet>
    {
        ["ls"] = OptionSet.Of(
            "aAbBcCdfFgGhHiklmnNopqQrRsStTuUvxXZ1",
            "ITw",
            $"all almost-all author escape ignore-backups color directory dired classify file-type full-time group-directories-first no-group human-readable si "
            + $"dereference-command-line dereference-command-line-symlink-to-dir hyperlink indicator-style inode kibibytes literal numeric-uid-gid "
            + $"hide-control-chars show-control-chars quote-name reverse recursive size context zero {Help}",
            "block-size format hide ignore quoting-style sort time time-style tabsize width"),
        ["cat"] = OptionSet.Of("AbeEnstTuv", string.Empty, $"show-all number-nonblank show-ends number squeeze-blank show-tabs show-nonprinting {Help}", string.Empty),
        ["head"] = OptionSet.Of("qvz", "cn", $"quiet silent verbose zero-terminated {Help}", "bytes lines") with { Digits = true },
        ["tail"] = OptionSet.Of("qvz", "cn", $"quiet silent verbose zero-terminated {Help}", "bytes lines") with { Digits = true },
        ["wc"] = OptionSet.Of("clmwL", string.Empty, $"bytes chars lines words max-line-length total {Help}", string.Empty),
        ["stat"] = OptionSet.Of("LftZ", "c", $"dereference file-system terse cached {Help}", "format printf"),
        ["cut"] = OptionSet.Of("snz", "bcdf", $"complement only-delimited zero-terminated {Help}", "bytes characters delimiter fields output-delimiter"),
        ["grep"] = Grep(),
        ["egrep"] = Grep(),
        ["fgrep"] = Grep(),
        ["rg"] = OptionSet.Of(
            "iSsFwxvcClnNHqoauU0",
            "egtTABCmMjr",
            "ignore-case smart-case case-sensitive fixed-strings word-regexp line-regexp invert-match count count-matches files files-with-matches files-without-match "
            + "line-number no-line-number with-filename no-filename hidden no-ignore no-ignore-vcs no-ignore-parent no-ignore-dot json vimgrep heading no-heading "
            + $"stats trim multiline multiline-dotall pcre2 only-matching type-list unrestricted text null column quiet no-messages passthru byte-offset crlf {Help}",
            "type type-not glob iglob max-count max-depth max-filesize context before-context after-context color colors sort sortr replace regexp threads max-columns "
            + "field-match-separator field-context-separator context-separator path-separator engine")
            with { TakesPattern = true, PatternOptions = OptionSet.Words("regexp files type-list") },
        ["file"] = OptionSet.Of("bEhiLNnrskv0", "eFP", $"brief mime mime-type mime-encoding dereference no-dereference no-pad raw special-files keep-going extension apple {Help}", "exclude separator parameter"),
        ["tree"] = OptionSet.Of(
            "adfxiqNQpugshDFvtcUrCnAJX",
            "LPIH",
            $"noreport dirsfirst du si inodes device prune matchdirs ignore-case info metafirst gitignore {Help}",
            "charset filelimit timefmt sort"),
        ["sort"] = OptionSet.Of("bdfgiMhnRrVcCmusz", "ktS", $"ignore-leading-blanks dictionary-order ignore-case general-numeric-sort ignore-nonprinting month-sort human-numeric-sort numeric-sort random-sort reverse version-sort check merge stable unique zero-terminated debug {Help}", "sort key field-separator buffer-size parallel"),
        ["uniq"] = OptionSet.Of("cdDiuz", "fsw", $"count repeated all-repeated ignore-case unique zero-terminated group {Help}", "skip-fields skip-chars check-chars") with { Most = 1 },
        ["pwd"] = OptionSet.Of("LP", string.Empty, Help, string.Empty) with { Most = 0 },
        ["echo"] = OptionSet.Of(string.Empty, string.Empty, string.Empty, string.Empty) with { Free = true, Paths = false },
        ["tr"] = OptionSet.Of(string.Empty, string.Empty, string.Empty, string.Empty) with { Free = true, Paths = false },
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static FrozenDictionary<string, OptionSet> Git { get; } = new Dictionary<string, OptionSet>
    {
        ["status"] = OptionSet.Of("sbvz", string.Empty, "short branch porcelain long verbose untracked-files ignored ignore-submodules column no-column ahead-behind no-ahead-behind renames no-renames", string.Empty) with { Attached = "u" },
        ["diff"] = OptionSet.Of("pusMCRbwaz", "SG", GitDiffing, "diff-filter inter-hunk-context stat-width") with { Digits = true, Attached = "U" },
        ["log"] = OptionSet.Of("pusMCRbwaiz", "nSG", $"{GitDiffing} {GitLogging}", GitLogValued) with { Digits = true, Attached = "U" },
        ["show"] = OptionSet.Of("pusMCRbwaiz", "nSG", $"{GitDiffing} {GitLogging}", GitLogValued) with { Digits = true, Attached = "U" },
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static FrozenSet<string> FindFlags { get; } = OptionSet.Words(
        "-H -P -depth -xdev -mount -not -and -or -a -o -print -print0 -prune -true -false -empty -readable -writable -executable -nouser -nogroup -ls -quit -daystart -noleaf ! ( ) ,");

    public static FrozenSet<string> FindValued { get; } = OptionSet.Words(
        "-name -iname -path -ipath -wholename -iwholename -regex -iregex -type -xtype -size -mtime -mmin -atime -amin -ctime -cmin -user -group -uid -gid -perm -maxdepth "
        + "-mindepth -links -inum -printf -regextype -lname -ilname -used -fstype");

    public static FrozenSet<string> FindPaths { get; } = OptionSet.Words("-newer -anewer -cnewer -samefile");

    private static OptionSet Grep() =>
        OptionSet.Of(
            "EFGPiyvwxcLlqsbHhnTZzrUaIo",
            "emABCdD",
            "extended-regexp fixed-strings basic-regexp perl-regexp ignore-case no-ignore-case word-regexp line-regexp null-data no-messages invert-match byte-offset line-number "
            + $"line-buffered with-filename no-filename only-matching quiet silent text recursive files-without-match files-with-matches count initial-tab null color colour binary {Help}",
            "regexp max-count label binary-files directories devices include exclude exclude-dir before-context after-context context")
            with { Digits = true, TakesPattern = true, PatternOptions = OptionSet.Words("regexp") };
}
