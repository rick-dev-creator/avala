using FastEndpoints;
using FluentValidation;

namespace Avala.Server.Activity.EndPoints.Ingestion;

internal sealed class IngestEventsValidator : Validator<IngestEventsRequest>
{
    public const int MaximumEvents = 500;

    public IngestEventsValidator() =>
        RuleFor(request => request.Events)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("the batch has no events")
            .Must(events => events!.Count is >= 1 and <= MaximumEvents)
            .WithMessage($"a batch holds 1 to {MaximumEvents} events");
}
