namespace Avala.Server.ArchitectureTests.Layout;

internal static class ServerScopes
{
    private static readonly ServerScope Production = new(BackendLayout.Modules);

    private static readonly ServerScope Violating = new(BackendLayout.ViolatingFixtures);

    public static ServerScope Of(Scope scope) => scope switch
    {
        Scope.Production => Production,
        Scope.Violating => Violating,
        _ => throw new ArgumentOutOfRangeException(nameof(scope)),
    };
}
