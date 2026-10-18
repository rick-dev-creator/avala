namespace Avala.Host.Tests;

internal sealed class Pacer(SimulatedRun run)
{
    private Task waiting = run.PacedAsync();

    public async Task<T> UntilAsync<T>(Task<T> until)
    {
        await UntilAsync((Task)until);

        return await until;
    }

    public async Task UntilAsync(Task until)
    {
        while (await Task.WhenAny(waiting, until) == waiting)
        {
            run.Pace();
            waiting = run.PacedAsync();
        }

        await until;
    }
}
