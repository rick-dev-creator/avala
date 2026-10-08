namespace Avala.ArchitectureTests.Scopes;

internal static class Layers
{
    private static readonly Layer[] Known =
        [Layer.Domain, Layer.Application, Layer.Infrastructure, Layer.ViewModels, Layer.Contracts];

    public static Layer Of(string? ns) =>
        Segments(ns).Select(Parse).FirstOrDefault(layer => layer != Layer.None);

    public static string ModuleOf(string? ns)
    {
        var segments = Segments(ns);
        var boundary = Array.FindIndex(segments, segment => Parse(segment) != Layer.None);

        return string.Join('.', boundary < 0 ? segments : segments[..boundary]);
    }

    private static string[] Segments(string? ns) => (ns ?? string.Empty).Split('.');

    private static Layer Parse(string segment) =>
        Known.FirstOrDefault(layer => layer.ToString() == segment, Layer.None);
}
