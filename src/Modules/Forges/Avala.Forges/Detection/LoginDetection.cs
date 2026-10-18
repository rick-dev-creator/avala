using Avala.Forges.Accounts;
using Avala.Forges.Contracts;
using Avala.Forges.Transport;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Forges.Detection;

internal interface IForgeProbes
{
    ValueTask<bool> RespondsAsync(Uri address, CancellationToken cancellationToken);
}

internal sealed class HttpForgeProbes(ForgeHttp http) : IForgeProbes
{
    public async ValueTask<bool> RespondsAsync(Uri address, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.Client.GetAsync(address, cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}

internal sealed class LoginDetection(IEnumerable<IForgeLogins> logins, IEnumerable<IForge> forges, IProcessRunner processes, IForgeProbes probes) : ILoginDetection
{
    public Func<string, Option<string>> Environment { get; init; } = _ => Option<string>.None;

    public async ValueTask<DetectedAccounts> DetectAsync(CancellationToken cancellationToken)
    {
        var accounts = new List<DetectedAccount>();
        var tools = new List<SignInTool>();

        foreach (var declared in logins)
        {
            var covered = declared.Probe.Match<IReadOnlyList<string>>(probe => [declared.Forge, probe.Forge], () => [declared.Forge]);
            accounts.AddRange(Tokens(declared));

            await declared.Logins.Match(
                async call =>
                {
                    var ran = await processes.RunAsync(new ProcessRequest(call.Command, call.Arguments), cancellationToken);
                    tools.Add(new SignInTool(call.Command, covered, declared.SignInCommand, ran.IsSuccess));
                    accounts.AddRange(await LoggedInAsync(declared, ran, cancellationToken));
                },
                () => Task.CompletedTask);
        }

        return new DetectedAccounts(accounts, tools);
    }

    private IEnumerable<DetectedAccount> Tokens(IForgeLogins declared) =>
        declared.TokenVariables
            .Where(variable => Environment(variable).Match(value => value.Length > 0, () => false))
            .Select(variable =>
            {
                var url = Info(declared.Forge).Bind(info => info.DefaultUrl);

                return new DetectedAccount(declared.Forge, url, url.Match(found => HostOf(declared.Forge, found), () => string.Empty), CredentialSource.Environment, variable, Option<string>.None);
            });

    private async Task<IReadOnlyList<DetectedAccount>> LoggedInAsync(IForgeLogins declared, Result<ProcessOutcome, ProcessError> ran, CancellationToken cancellationToken)
    {
        if (!ran.TryGetValue(out var outcome, out _))
        {
            return [];
        }

        var found = new List<DetectedAccount>();

        foreach (var login in declared.Read(outcome))
        {
            var forge = await declared.Probe.Match(
                async probe => await probes.RespondsAsync(new Uri($"{login.Url.AbsoluteUri.TrimEnd('/')}{probe.Path}"), cancellationToken) ? probe.Forge : declared.Forge,
                () => Task.FromResult(declared.Forge));
            found.Add(new DetectedAccount(forge, login.Url, HostOf(forge, login.Url), CredentialSource.Cli, login.Reference, login.Login.Length > 0 ? login.Login : Option<string>.None));
        }

        return found;
    }

    private Option<ForgeInfo> Info(string forge) => forges.FirstOrDefault(found => found.Info.Id == forge).ToOption().Map(found => found.Info);

    private string HostOf(string forge, Uri url) =>
        forges.FirstOrDefault(found => found.Info.Id == forge) is IForgeRepositories hosting ? hosting.GitHost(url) : url.Host;
}
