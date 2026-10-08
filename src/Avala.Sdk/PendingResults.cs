namespace Avala.Sdk;

public static class PendingResults
{
    extension<TValue, TError>(Task<Result<TValue, TError>> pending)
        where TError : struct, Enum
    {
        public async Task<Result<TNext, TError>> BindAsync<TNext>(Func<TValue, Result<TNext, TError>> bind) =>
            (await pending).Bind(bind);

        public async Task<Result<TNext, TError>> BindAsync<TNext>(Func<TValue, Task<Result<TNext, TError>>> bind) =>
            await (await pending).BindAsync(bind);

        public async Task<Result<TNext, TError>> MapAsync<TNext>(Func<TValue, TNext> map) =>
            (await pending).Map(map);
    }
}
