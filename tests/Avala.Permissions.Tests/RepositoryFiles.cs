using Avala.Jobs.Contracts;
using Avala.Permissions.Answering;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Permissions.Tests;

internal sealed class RepositoryFiles : IRepositoryRuleFiles
{
    public List<(JobId, PolicyRule)> Added { get; } = [];

    public Option<PolicyError> Refusal { get; set; }

    public Task<Result<PolicyRule, PolicyError>> AddAsync(JobId job, PolicyRule rule, CancellationToken cancellationToken)
    {
        Added.Add((job, rule));

        return Task.FromResult(Refusal.Match(Result<PolicyRule, PolicyError>.Failure, () => Result<PolicyRule, PolicyError>.Success(rule)));
    }
}
