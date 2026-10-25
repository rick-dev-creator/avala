using System.Collections.Concurrent;
using System.Reflection;
using Ardalis.Result;

namespace Avala.Server.CrossCuttingConcerns.Validation;

internal static class InvalidResults
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> Factories = new();

    public static TResponse Of<TResponse>(IEnumerable<ValidationError> errors)
        where TResponse : IResult =>
        (TResponse)Factories.GetOrAdd(typeof(TResponse), FactoryOf).Invoke(null, [errors])!;

    private static MethodInfo FactoryOf(Type result) =>
        result.GetMethod(nameof(Result.Invalid), BindingFlags.Public | BindingFlags.Static, [typeof(IEnumerable<ValidationError>)])
        ?? throw new InvalidOperationException($"{result} has no Invalid factory taking validation errors.");
}
