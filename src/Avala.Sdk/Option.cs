namespace Avala.Sdk;

public readonly record struct Option<T>
    where T : notnull
{
    private readonly T value;

    private Option(T value)
    {
        this.value = value;
        IsSome = true;
    }

    public static Option<T> None => default;

    public bool IsSome { get; }

    public bool IsNone => !IsSome;

    public static Option<T> Some(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new Option<T>(value);
    }

    public static implicit operator Option<T>(T value) => Some(value);

    public TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none) =>
        IsSome ? some(value) : none();

    public Option<TNext> Map<TNext>(Func<T, TNext> map)
        where TNext : notnull =>
        IsSome ? Option<TNext>.Some(map(value)) : Option<TNext>.None;

    public Option<TNext> Bind<TNext>(Func<T, Option<TNext>> bind)
        where TNext : notnull =>
        IsSome ? bind(value) : Option<TNext>.None;

    public Result<T, TError> ToResult<TError>(TError error)
        where TError : struct, Enum =>
        IsSome ? Result<T, TError>.Success(value) : Result<T, TError>.Failure(error);
}
