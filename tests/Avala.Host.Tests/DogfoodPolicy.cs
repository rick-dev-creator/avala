using System.Reflection;
using Avala.Agents.Contracts.Events;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal static partial class DogfoodPolicy
{
    public static (bool Allow, string Reason) Judge(ItemKind kind, string target, string worktree)
    {
        var inside = worktree.Length > 0 && (target.StartsWith(worktree, StringComparison.Ordinal) || !Path.IsPathRooted(target));

        return kind switch
        {
            ItemKind.Web => (false, "No network access in this job: work offline."),
            ItemKind.Mcp => (false, "No MCP tools in this job."),
            ItemKind.FileEdit or ItemKind.Search or ItemKind.Other when !inside => (false, "Stay inside the repository's worktree."),
            ItemKind.Command when Refused.FirstOrDefault(word => target.Contains(word, StringComparison.Ordinal)) is { } word =>
                (false, $"Not allowed here ('{word.Trim()}'): no servers, background processes, global or networked installs, or remote git."),
            ItemKind.Command when OutsidePaths(target, worktree) is { Count: > 0 } outside => (false, $"Stay inside the repository's worktree (touches {string.Join(", ", outside)})."),
            _ => (true, "A reasonable development action inside the worktree."),
        };
    }

    public static bool Chained(string command) =>
        command.Contains("&&", StringComparison.Ordinal) || command.Contains("||", StringComparison.Ordinal) || command.Contains(';', StringComparison.Ordinal)
        || command.Contains('|', StringComparison.Ordinal) || command.Contains('\n', StringComparison.Ordinal) || command.Contains("$(", StringComparison.Ordinal)
        || command.Contains('`', StringComparison.Ordinal) || command.Contains('>', StringComparison.Ordinal);

    private static List<string> OutsidePaths(string command, string worktree) =>
        [.. HeredocBody().Replace(command, " ${rest}").Split([' ', '\t', '\n', '"', '\'', '=', ';', '(', ')', '|', '>', '<'], StringSplitOptions.RemoveEmptyEntries)
            .Where(token => PathLike().IsMatch(token))
            .Where(token => token.Length > 1 && token[1] != '/')
            .Where(token => token.StartsWith('/') || token.StartsWith('~') || token.StartsWith("$HOME", StringComparison.Ordinal))
            .Where(token => !(worktree.Length > 0 && token.StartsWith(worktree, StringComparison.Ordinal)))
            .Where(token => token is not "/dev/null" && !token.StartsWith("/usr/bin/", StringComparison.Ordinal) && !token.StartsWith("/bin/", StringComparison.Ordinal))];

    [System.Text.RegularExpressions.GeneratedRegex(@"<<-?\s*['""]?(\w+)['""]?(?<rest>[^\n]*)\n[\s\S]*?^\s*\1[ \t]*$", System.Text.RegularExpressions.RegexOptions.Multiline)]
    private static partial System.Text.RegularExpressions.Regex HeredocBody();

    [System.Text.RegularExpressions.GeneratedRegex(@"^(~|\$HOME|/)[\w./~$-]*$")]
    private static partial System.Text.RegularExpressions.Regex PathLike();

    public static string Describe(object target) =>
        string.Join(", ", target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0 && (property.PropertyType == typeof(string) || property.PropertyType == typeof(bool) || property.PropertyType.IsAssignableTo(typeof(System.Collections.IEnumerable))))
            .Select(property => (property.Name, Value: property.GetValue(target)))
            .Select(pair => pair.Value switch
            {
                string text => $"{pair.Name}='{text}'",
                bool flag => $"{pair.Name}={flag}",
                System.Collections.IEnumerable list => $"{pair.Name}=[{string.Join(" | ", list.Cast<object>().Take(12).Select(item => item is string ? item : Flat(item)))}]",
                _ => string.Empty,
            })
            .Where(text => text.Length > 0));

    private static string Flat(object item) =>
        string.Join(" ", item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0 && property.PropertyType == typeof(string))
            .Select(property => property.GetValue(item) as string)
            .Where(text => !string.IsNullOrEmpty(text)));
}
