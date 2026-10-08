namespace Avala.Testing;

public static class Repository
{
    public static DirectoryInfo Root { get; } = Find(new DirectoryInfo(AppContext.BaseDirectory));

    private static DirectoryInfo Find(DirectoryInfo directory) =>
        File.Exists(Path.Combine(directory.FullName, "Avala.slnx"))
            ? directory
            : Find(directory.Parent ?? throw new InvalidOperationException("Avala.slnx not found above the test output."));
}
