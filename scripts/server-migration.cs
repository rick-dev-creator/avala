#:property PublishAot=false
#:package Microsoft.EntityFrameworkCore.Design
#:package Npgsql.EntityFrameworkCore.PostgreSQL
#:project ../backend/Fleet/Modules/Instances/Avala.Server.Instances.Infrastructure/Avala.Server.Instances.Infrastructure.csproj

using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;

if (args is not [var module, var name])
{
    await Console.Error.WriteLineAsync("Usage: dotnet run --no-cache scripts/server-migration.cs -- <Module> <MigrationName>");

    return 1;
}

var root = Repository.Root().FullName;
var infrastructure = $"Avala.Server.{module}.Infrastructure";
var assembly = Assembly.Load(new AssemblyName(infrastructure));
var contextType = assembly.GetTypes().Single(type => typeof(DbContext).IsAssignableFrom(type));
var options = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType))!;
options.UseNpgsql("Host=design");

await using var context = (DbContext)Activator.CreateInstance(contextType, options.Options)!;
var migration = Scaffolding.Scaffold(context, infrastructure, name);
var folder = Path.Combine(root, "backend", "Fleet", "Modules", module, infrastructure, "Storage", "Migrations");
Directory.CreateDirectory(folder);

await Generated.WriteAsync(Path.Combine(folder, $"{migration.MigrationId}.g.cs"), migration.MigrationCode);
await Generated.WriteAsync(Path.Combine(folder, $"{migration.MigrationId}.Designer.g.cs"), migration.MetadataCode);
await Generated.WriteAsync(Path.Combine(folder, $"{migration.SnapshotName}.g.cs"), migration.SnapshotCode);
Console.WriteLine($"Added {migration.MigrationId} to {Path.GetRelativePath(root, folder)}");

return 0;

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
        var npgsql = typeof(NpgsqlDbContextOptionsBuilderExtensions).Assembly;
        var declared = npgsql.GetCustomAttribute<DesignTimeProviderServicesAttribute>()
            ?? throw new InvalidOperationException("The Npgsql provider declares no design-time services.");

        return (IDesignTimeServices)Activator.CreateInstance(npgsql.GetType(declared.TypeName, throwOnError: true)!)!;
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
