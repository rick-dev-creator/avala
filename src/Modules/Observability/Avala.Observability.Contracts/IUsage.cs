using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Observability.Contracts;

public interface IUsage
{
    IReadOnlyList<ProviderUsage> ByProvider();

    IReadOnlyList<AccountUsage> ByAccount();

    IReadOnlyList<ConnectionUsage> ByConnection();

    Option<UsageSummary> OfSession(SessionId session);

    Option<UsageSummary> OfJob(JobId job);
}
