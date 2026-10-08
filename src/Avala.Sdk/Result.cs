using System.Diagnostics.CodeAnalysis;

namespace Avala.Sdk;

public sealed class Result<TValue, TError>
    where TError : struct, Enum
{
    private readonly TValue? value;
    private readonly TError error;

    private Result(TValue? value, TError error, bool isSuccess)
    {
        this.value = value;
        this.error = error;
        IsSuccess = isSuccess;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public static Result<TValue, TError> Success(TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new Result<TValue, TError>(value, default, true);
    }

    public static Result<TValue, TError> Failure(TError error) => new(default, error, false);

    public static implicit operator Result<TValue, TError>(TValue value) => Success(value);

    public static implicit operator Result<TValue, TError>(TError error) => Failure(error);

    public TResult Match<TResult>(Func<TValue, TResult> onSuccess, Func<TError, TResult> onFailure) =>
        IsSuccess ? onSuccess(value!) : onFailure(error);

    public bool TryGetValue([MaybeNullWhen(false)] out TValue value, out TError error)
    {
        value = this.value;
        error = this.error;

        return IsSuccess;
    }

    public Result<TNext, TError> Map<TNext>(Func<TValue, TNext> map) =>
        IsSuccess ? Result<TNext, TError>.Success(map(value!)) : Result<TNext, TError>.Failure(error);

    public Result<TNext, TError> Bind<TNext>(Func<TValue, Result<TNext, TError>> bind) =>
        IsSuccess ? bind(value!) : Result<TNext, TError>.Failure(error);

    public async Task<Result<TNext, TError>> BindAsync<TNext>(Func<TValue, Task<Result<TNext, TError>>> bind) =>
        IsSuccess ? await bind(value!) : Result<TNext, TError>.Failure(error);

    public Result<TValue, TNextError> MapError<TNextError>(Func<TError, TNextError> map)
        where TNextError : struct, Enum =>
        IsSuccess ? Result<TValue, TNextError>.Success(value!) : Result<TValue, TNextError>.Failure(map(error));
}
