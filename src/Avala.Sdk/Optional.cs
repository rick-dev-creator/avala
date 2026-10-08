namespace Avala.Sdk;

public static class Optional
{
    extension<T>(T? value)
        where T : class
    {
        public Option<T> ToOption() => value is null ? Option<T>.None : Option<T>.Some(value);
    }

    extension<T>(T? value)
        where T : struct
    {
        public Option<T> ToOption() => value is { } present ? Option<T>.Some(present) : Option<T>.None;
    }

    extension<T>(Task<Option<T>> pending)
        where T : notnull
    {
        public async Task<TResult> MatchAsync<TResult>(Func<T, Task<TResult>> some, Func<Task<TResult>> none) =>
            await (await pending).Match(some, none);

        public async Task MatchAsync(Func<T, Task> some, Func<Task> none) =>
            await (await pending).Match(some, none);
    }
}
