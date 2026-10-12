using System.Text.RegularExpressions;
using Avala.Forges.Contracts;
using Avala.GitHub.Api;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.GitHub.SignIn;

internal sealed partial class GhLogins : IForgeLogins
{
    private const string PublicHost = "github.com";

    public string Forge => GitHubForge.Id;

    public Option<CliCall> Logins { get; } = new CliCall("gh", ["auth", "status"], CliStatusChannel.Output);

    public Option<string> SignInCommand { get; } = "gh auth login";

    public IReadOnlyList<string> TokenVariables { get; } = ["GH_TOKEN", "GITHUB_TOKEN"];

    public Option<ForgeProbe> Probe => Option<ForgeProbe>.None;

    public IReadOnlyList<DetectedLogin> Read(ProcessOutcome outcome)
    {
        var found = new List<DetectedLogin>();

        foreach (var line in $"{outcome.Output}\n{outcome.Error}".ReplaceLineEndings("\n").Split('\n'))
        {
            if (LoggedIn().Match(line) is { Success: true } login && !line.Contains("Failed", StringComparison.OrdinalIgnoreCase))
            {
                found.Add(Of(login.Groups["host"].Value, login.Groups["login"].Value));
            }
            else if (Inactive().IsMatch(line) && found.Count > 0)
            {
                found.RemoveAt(found.Count - 1);
            }
        }

        return found;
    }

    private static DetectedLogin Of(string host, string login) =>
        string.Equals(host, PublicHost, StringComparison.OrdinalIgnoreCase)
            ? new DetectedLogin(GitHubForge.Public, login)
            : new DetectedLogin(new Uri($"https://{host}/api/v3"), login) { Reference = host };

    [GeneratedRegex(@"Logged in to (?<host>[^\s]+) (?:account|as) (?<login>[^\s(]+)")]
    private static partial Regex LoggedIn();

    [GeneratedRegex(@"Active account:\s*false", RegexOptions.IgnoreCase)]
    private static partial Regex Inactive();
}
