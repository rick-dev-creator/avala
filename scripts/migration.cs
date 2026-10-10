#:property PublishAot=false
#:package Microsoft.EntityFrameworkCore.Design
#:project ../src/Modules/Jobs/Avala.Jobs/Avala.Jobs.csproj
#:project ../src/Modules/Workspaces/Avala.Workspaces/Avala.Workspaces.csproj
#:project ../src/Modules/Observability/Avala.Observability/Avala.Observability.csproj
#:project ../src/Modules/Supervision/Avala.Supervision/Avala.Supervision.csproj
#:project ../src/Modules/Budgets/Avala.Budgets/Avala.Budgets.csproj
#:project ../src/Modules/Autopilot/Avala.Autopilot/Avala.Autopilot.csproj
#:project ../src/Modules/Verification/Avala.Verification/Avala.Verification.csproj
#:project ../src/Modules/Permissions/Avala.Permissions/Avala.Permissions.csproj
#:project ../src/Modules/Delegation/Avala.Delegation/Avala.Delegation.csproj
#:project ../src/Modules/Transcripts/Avala.Transcripts/Avala.Transcripts.csproj
#:project ../src/Modules/Handoffs/Avala.Handoffs/Avala.Handoffs.csproj
#:project ../src/Modules/Triggers/Avala.Triggers/Avala.Triggers.csproj

using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;

if (args is not [var module, var name])
{
    await Console.Error.WriteLineAsync("Usage: dotnet run --no-cache scripts/migration.cs -- <Module> <MigrationName>");

    return 1;
}

var root = Repository.Root().FullName;
var assembly = Assembly.Load(new AssemblyName($"Avala.{module}"));
var contextType = assembly.GetTypes().Single(type => typeof(DbContext).IsAssignableFrom(type));
var scratch = Directory.CreateTempSubdirectory("avala-migration-");

try
{
    await using var context = (DbContext)Activator.CreateInstance(contextType, Path.Combine(scratch.FullName, "design.db"))!;
    var migration = Scaffolding.Scaffold(context, assembly.GetName().Name!, name);
    var folder = Path.Combine(root, "src", "Modules", module, $"Avala.{module}", "Storage", "Migrations");
    Directory.CreateDirectory(folder);

    await Generated.WriteAsync(Path.Combine(folder, $"{migration.MigrationId}.g.cs"), migration.MigrationCode);
    await Generated.WriteAsync(Path.Combine(folder, $"{migration.MigrationId}.Designer.g.cs"), migration.MetadataCode);
    await Generated.WriteAsync(Path.Combine(folder, $"{migration.SnapshotName}.g.cs"), migration.SnapshotCode);
    Console.WriteLine($"Added {migration.MigrationId} to {Path.GetRelativePath(root, folder)}");

    return 0;
}
finally
{
    scratch.Delete(recursive: true);
}

internal static class Scaffolding
{
    public static ScaffoldedMigration Scaffold(DbContext context, string rootNamespace, string name)
    {
        var services = new ServiceCollection()
            .AddEntityFrameworkDesignTimeServices()
            .AddDbContextDesignTimeServices(context);
        Provider().ConfigureDesignTimeServices(services);

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IMigrationsScaffolder>().ScaffoldMigration(name, rootNamespace, "Storage.Migrations", "C#");
    }

    private static IDesignTimeServices Provider()
    {
        var sqlite = typeof(SqliteDbContextOptionsBuilderExtensions).Assembly;
        var declared = sqlite.GetCustomAttribute<DesignTimeProviderServicesAttribute>()
            ?? throw new InvalidOperationException("The SQLite provider declares no design-time services.");

        return (IDesignTimeServices)Activator.CreateInstance(sqlite.GetType(declared.TypeName, throwOnError: true)!)!;
    }
}

internal static partial class Generated
{
    public static Task WriteAsync(string path, string code) =>
        File.WriteAllTextAsync(path, Conform(code));

    private static string Conform(string code) =>
        string.Join(
            '\n',
            code.ReplaceLineEndings("\n")
                .Split('\n')
                .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                .Select(line => PartialClass().Replace(line, "${indent}internal sealed partial class ")));

    [GeneratedRegex(@"^(?<indent>\s*)(public\s+)?partial class ")]
    private static partial Regex PartialClass();
}

internal static class Repository
{
    public static DirectoryInfo Root() => Find(new DirectoryInfo(Directory.GetCurrentDirectory()));

    private static DirectoryInfo Find(DirectoryInfo directory) =>
        File.Exists(Path.Combine(directory.FullName, "Avala.slnx"))
            ? directory
            : Find(directory.Parent ?? throw new InvalidOperationException("Run the script inside the repository."));
}
