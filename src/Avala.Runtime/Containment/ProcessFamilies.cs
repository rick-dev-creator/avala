namespace Avala.Runtime.Containment;

internal readonly record struct Kin(int Id, int Parent);

internal static class ProcessFamilies
{
    public static IReadOnlyList<int> Of(IReadOnlyCollection<Kin> processes, IEnumerable<int> roots)
    {
        var members = roots.ToHashSet();

        while (processes.Where(process => !members.Contains(process.Id) && members.Contains(process.Parent)).ToList() is { Count: > 0 } descendants)
        {
            members.UnionWith(descendants.Select(descendant => descendant.Id));
        }

        return [.. members];
    }
}
