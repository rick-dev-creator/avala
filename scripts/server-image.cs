using System.Diagnostics;

var tag = args.Length == 2 && args[0] == "--tag" ? args[1] : string.Empty;

if (!tag.StartsWith('v') || tag.Length < 2)
{
    await Console.Error.WriteLineAsync("Usage: dotnet run scripts/server-image.cs -- --tag v<version>");

    return 2;
}

var version = tag[1..];
var tags = version.Contains('-', StringComparison.Ordinal) ? version : $"{version}%3Blatest";
var root = Root(Environment.CurrentDirectory);
var publish = new ProcessStartInfo("dotnet") { WorkingDirectory = root };

foreach (var argument in new[]
{
    "publish", "backend/Fleet/Avala.Server", "-t:PublishContainer", "-c", "Release",
    "-p:ContainerRegistry=ghcr.io", "-p:ContainerRepository=rick-dev-creator/avala-server",
    $"-p:ContainerImageTags={tags}", $"-p:Version={version}",
})
{
    publish.ArgumentList.Add(argument);
}

using var process = Process.Start(publish) ?? throw new InvalidOperationException("dotnet could not start.");
await process.WaitForExitAsync();
Console.WriteLine(process.ExitCode == 0 ? $"Published ghcr.io/rick-dev-creator/avala-server:{tags.Replace("%3B", ", ", StringComparison.Ordinal)}" : "The fleet server image was not published.");

return process.ExitCode;

static string Root(string directory) =>
    File.Exists(Path.Combine(directory, "Avala.slnx")) ? directory : Root(Path.GetDirectoryName(directory) ?? throw new InvalidOperationException("Run it from inside the Avala repository."));
