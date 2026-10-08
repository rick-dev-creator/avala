namespace Avala.Fixtures.Violating.Domain;

public sealed class DiskPolicy
{
    public bool IsArchived(string path) => File.Exists(path);
}
