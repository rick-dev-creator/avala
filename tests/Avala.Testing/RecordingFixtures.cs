namespace Avala.Testing;

public static class RecordingFixtures
{
    public const string RefreshVariable = "AVALA_UPDATE_RECORDINGS";

    private const string Expectations = ".expected.json";

    public static string Folder { get; } = Path.Combine(Repository.Root.FullName, "tests", "recordings");

    public static IReadOnlyList<string> Names =>
        [.. Directory.EnumerateFiles(Folder, $"*{Expectations}").Select(path => Path.GetFileName(path)[..^Expectations.Length]).Order(StringComparer.Ordinal)];

    public static bool Refreshing => Environment.GetEnvironmentVariable(RefreshVariable) == "1";

    public static string RecordingOf(string name) => Path.Combine(Folder, $"{name}.json");

    public static string ExpectationsOf(string name) => Path.Combine(Folder, $"{name}{Expectations}");

    public static string Replay(string name) => $"[replay: {name}] Greet the team";
}
