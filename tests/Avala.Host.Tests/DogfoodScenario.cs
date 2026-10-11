using Avala.Sdk;

namespace Avala.Host.Tests;

internal sealed record DogfoodScenario(string Name, string Title, string Feature, decimal Cap, TimeSpan Guard)
{
    public bool Optional { get; init; }

    public bool UsesOther { get; init; }

    public string MainModel { get; init; } = "haiku";

    public string OtherModel { get; init; } = "haiku";

    public string Rehearsal { get; init; } = string.Empty;

    public Func<DogfoodSetup, DogfoodSeed> Seed { get; init; } = _ => new DogfoodSeed();

    public Func<DogfoodScene, Task> Steps { get; init; } = _ => Task.CompletedTask;

    public IReadOnlyList<DogfoodCheck> Checks { get; init; } = [];
}

internal sealed record DogfoodSetup(DogfoodPlace Place, bool Rehearsal, double Threshold, decimal Cap)
{
    public string Tagged(string tag, string instruction) => Rehearsal ? $"{tag} {instruction}" : instruction;
}

internal sealed record DogfoodSeed
{
    public IReadOnlyList<DogfoodFile> Committed { get; init; } = [];

    public IReadOnlyList<DogfoodFile> Data { get; init; } = [];

    public IReadOnlyList<DogfoodScript> Recordings { get; init; } = [];

    public bool Origin { get; init; }
}

internal sealed record DogfoodFile(string Path, string Content);

internal sealed record DogfoodPlace(string Folder, string Screens)
{
    public string Repository => System.IO.Path.Combine(Folder, "repository");

    public string Data => System.IO.Path.Combine(Folder, "data");

    public string Report => System.IO.Path.Combine(Folder, "report");

    public string Origin => System.IO.Path.Combine(Folder, "origin.git");

    public string Logins => System.IO.Path.Combine(Folder, "logins");

    public string Name => System.IO.Path.GetFileName(Folder);
}

internal sealed record DogfoodCheck(string Then, Func<DogfoodEvidence, DogfoodVerdict> Judge);

internal enum DogfoodMark
{
    Pass,
    Fail,
    NotReached,
}

internal sealed record DogfoodVerdict(DogfoodMark Mark, string Evidence)
{
    public static DogfoodVerdict Pass(string evidence) => new(DogfoodMark.Pass, evidence);

    public static DogfoodVerdict Fail(string evidence) => new(DogfoodMark.Fail, evidence);

    public static DogfoodVerdict NotReached(string why) => new(DogfoodMark.NotReached, why);

    public static DogfoodVerdict When(bool holds, string evidence) => new(holds ? DogfoodMark.Pass : DogfoodMark.Fail, evidence);

    public static DogfoodVerdict Unless<T>(Option<T> precondition, string missing, Func<T, DogfoodVerdict> judge)
        where T : notnull =>
        precondition.Match(judge, () => NotReached(missing));
}
