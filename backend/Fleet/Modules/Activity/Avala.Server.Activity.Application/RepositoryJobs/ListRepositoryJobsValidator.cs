using Avala.Server.Activity.Contracts.RepositoryJobs;
using Avala.Server.Activity.Domain;
using FluentValidation;

namespace Avala.Server.Activity.Application.RepositoryJobs;

internal sealed class ListRepositoryJobsValidator : AbstractValidator<ListRepositoryJobs>
{
    public ListRepositoryJobsValidator() =>
        RuleFor(query => query.Key).Custom((key, context) =>
        {
            if (RepositoryKey.Create(key).Refusal is { } refusal)
            {
                context.AddFailure(nameof(ListRepositoryJobs.Key), refusal);
            }
        });
}
