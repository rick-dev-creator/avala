using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Permissions.Answering;

internal interface IRepositoryRuleFiles
{
    Task<Result<PolicyRule, PolicyError>> AddAsync(JobId job, PolicyRule rule, CancellationToken cancellationToken);
}
