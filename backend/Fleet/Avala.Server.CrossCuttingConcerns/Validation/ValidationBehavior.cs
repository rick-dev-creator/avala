using Ardalis.Result;
using FluentValidation;
using Mediator;

namespace Avala.Server.CrossCuttingConcerns.Validation;

public sealed class ValidationBehavior<TMessage, TResponse>(IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
    where TResponse : IResult
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        List<ValidationError> errors = [];

        foreach (var validator in validators)
        {
            var outcome = await validator.ValidateAsync(message, cancellationToken);
            errors.AddRange(outcome.Errors.Select(failure =>
                new ValidationError(failure.PropertyName, failure.ErrorMessage, failure.ErrorCode, ValidationSeverity.Error)));
        }

        return errors.Count == 0 ? await next(message, cancellationToken) : InvalidResults.Of<TResponse>(errors);
    }
}
