namespace Avala.Server.Violating.Domain;

internal class Ledger
{
    public bool IsArchived(string path) => File.Exists(path);
}
