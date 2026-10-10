using System.Globalization;
using System.Text.RegularExpressions;
using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Forges.Transport;

internal sealed partial class CliForgeApi(IProcessRunner processes, IForge forge, ForgeTarget target) : IForgeApi
{
    public async ValueTask<Result<ForgeResponse, ForgeError>> SendAsync(ForgeRequest request, CancellationToken cancellationToken) =>
        await forge.CliFor(request, target).Match(
            call => RunAsync(call, cancellationToken),
            () => Task.FromResult(Result<ForgeResponse, ForgeError>.Failure(ForgeError.CliUnavailable)));

    public static Result<ForgeResponse, ForgeError> Read(CliCall call, ProcessOutcome outcome)
    {
        var headers = call.Status == CliStatusChannel.Output ? outcome.Output : outcome.Error;
        var status = StatusLine().Match(headers);

        if (!status.Success)
        {
            return outcome.Succeeded ? new ForgeResponse(200, outcome.Output) : ForgeError.Unreachable;
        }

        var code = int.Parse(status.Groups["code"].Value, CultureInfo.InvariantCulture);
        var body = call.Status == CliStatusChannel.Output ? AfterHeaders(outcome.Output) : outcome.Output;
        var exhausted = RateLimitExhausted().IsMatch(headers);

        return Statuses.Of(code, body, exhausted);
    }

    private static string AfterHeaders(string output)
    {
        var normalized = output.ReplaceLineEndings("\n");
        var blank = normalized.IndexOf("\n\n", StringComparison.Ordinal);

        return blank < 0 ? string.Empty : normalized[(blank + 2)..];
    }

    private async Task<Result<ForgeResponse, ForgeError>> RunAsync(CliCall call, CancellationToken cancellationToken) =>
        (await processes.RunAsync(new ProcessRequest(call.Command, call.Arguments), cancellationToken)).Match(
            outcome => Read(call, outcome),
            _ => ForgeError.CliUnavailable);

    [GeneratedRegex(@"HTTP/\S+\s+(?<code>\d{3})")]
    private static partial Regex StatusLine();

    [GeneratedRegex(@"(?im)^x-ratelimit-remaining:\s*0\s*$")]
    private static partial Regex RateLimitExhausted();
}

internal sealed class ForgeTransports(IProcessRunner processes, ForgeHttp http) : IForgeTransports
{
    public Func<string, Option<string>> Environment { get; init; } = variable => System.Environment.GetEnvironmentVariable(variable).ToOption();

    public IForgeApi Open(ForgeDeclaration declaration, IForge forge, ForgeTarget target) => declaration.Source switch
    {
        CredentialSource.Cli => new CliForgeApi(processes, forge, target),
        CredentialSource.Environment => new HttpForgeApi(http.Client, target.Api, declaration.Reference.Map(variable => new ForgeToken(forge.Info.TokenScheme, variable)), Environment),
        _ => new HttpForgeApi(http.Client, target.Api, Option<ForgeToken>.None, Environment),
    };

    public Option<ForgeError> Problem(ForgeDeclaration declaration) =>
        declaration.Source == CredentialSource.Environment && declaration.Reference.Match(variable => Environment(variable).Match(value => value.Length == 0, () => true), () => true)
            ? ForgeError.MissingCredential
            : Option<ForgeError>.None;
}

internal sealed class ForgeHttp : IDisposable
{
    public static TimeSpan Patience { get; } = TimeSpan.FromSeconds(30);

    private readonly Lazy<HttpClient> client = new(() => new HttpClient { Timeout = Patience });

    public HttpClient Client => client.Value;

    public void Dispose()
    {
        if (client.IsValueCreated)
        {
            client.Value.Dispose();
        }
    }
}
