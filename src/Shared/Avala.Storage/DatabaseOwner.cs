using Avala.Sdk;
using Microsoft.EntityFrameworkCore;

namespace Avala.Storage;

public sealed class DatabaseOwner<TContext>(string file, Func<string, TContext> create) : IAsyncDisposable
    where TContext : DbContext
{
    private readonly SerialExecutor serial = new();
    private TContext? context;

    public Task<T> RunAsync<T>(Func<TContext, Task<T>> work, CancellationToken cancellationToken) =>
        serial.RunAsync(token => Task.Run(async () => await work(await OpenAsync(token)), token), cancellationToken);

    public Task OpenedAsync(CancellationToken cancellationToken) => RunAsync(_ => Task.FromResult(true), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await serial.DisposeAsync();

        if (context is not null)
        {
            await context.DisposeAsync();
        }
    }

    private async Task<TContext> OpenAsync(CancellationToken cancellationToken)
    {
        if (context is null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var opening = create(file);

            try
            {
                await ModuleDatabase.MigrateAsync(opening, cancellationToken);
            }
            catch
            {
                await opening.DisposeAsync();
                throw;
            }

            context = opening;
        }

        return context;
    }
}
