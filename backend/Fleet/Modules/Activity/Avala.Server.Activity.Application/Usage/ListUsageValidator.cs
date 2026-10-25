using Avala.Server.Activity.Contracts.Usage;
using Avala.Server.Activity.Domain;
using FluentValidation;

namespace Avala.Server.Activity.Application.Usage;

internal sealed class ListUsageValidator : AbstractValidator<ListUsage>
{
    public ListUsageValidator() =>
        RuleFor(query => query).Custom((query, context) =>
        {
            foreach (var refusal in UsageRequests.Read(query.From, query.To, query.Zone, query.Step, query.Repository).Refusals)
            {
                context.AddFailure(refusal.Field, refusal.Reason);
            }
        });
}
