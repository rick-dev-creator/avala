if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run scripts/publish-summary.cs -- <markdown-file>...");
    return 1;
}

var sections = await Task.WhenAll(args.Select(path => File.ReadAllTextAsync(path)));
var summary = string.Join(Environment.NewLine, sections);

if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } target)
{
    await File.AppendAllTextAsync(target, summary);
}
else
{
    Console.WriteLine(summary);
}

return 0;
