namespace Avala.ArchitectureTests.Scopes;

internal static class Layers
{
    public static Layer Of(string? ns) => Find(ns)?.Layer ?? Layer.None;

    public static string ModuleOf(string? ns) => Find(ns)?.Module ?? ns ?? string.Empty;

    public static bool IsDeclared(string? ns) => Find(ns) is not null;

    private static Placement? Find(string? ns) => ns is not null ? LayerMap.Placements.GetValueOrDefault(ns) : null;
}
