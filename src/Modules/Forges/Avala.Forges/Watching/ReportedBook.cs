using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Forges.Watching;

internal sealed record KeptReport(JobId Job, Reported Reported);

internal interface IReportedStore
{
    Task<IReadOnlyList<KeptReport>> ReportsAsync(CancellationToken cancellationToken);

    Task KeepReportAsync(KeptReport report, CancellationToken cancellationToken);

    Task ForgetReportAsync(JobId job, CancellationToken cancellationToken);
}

internal sealed class ReportedBook(IReportedStore store) : IStartupTask, IAsyncDisposable
{
    private readonly ReportedLinks links = new();
    private readonly SerialExecutor serial = new();

    public Task RunAsync(CancellationToken cancellationToken) =>
        serial.RunAsync(
            async token =>
            {
                foreach (var kept in await store.ReportsAsync(token))
                {
                    links.Restore(kept.Job, kept.Reported);
                }
            },
            cancellationToken);

    public Task StartedAsync(JobId job, SessionId session, CancellationToken cancellationToken) =>
        serial.RunAsync(
            _ =>
            {
                links.Started(job, session);

                return Task.CompletedTask;
            },
            cancellationToken);

    public Task<Reported> OfAsync(JobId job, CancellationToken cancellationToken) =>
        serial.RunAsync(_ => Task.FromResult(links.Of(job)), cancellationToken);

    public Task SawAsync(IAgentEvent activity, CancellationToken cancellationToken) =>
        serial.RunAsync(
            token => links.Saw(activity).Match(
                job => store.KeepReportAsync(new KeptReport(job, links.Of(job)), token),
                () => Task.CompletedTask),
            cancellationToken);

    public Task ForgetAsync(JobId job, CancellationToken cancellationToken) =>
        serial.RunAsync(
            token =>
            {
                links.Forget(job);

                return store.ForgetReportAsync(job, token);
            },
            cancellationToken);

    public ValueTask DisposeAsync() => serial.DisposeAsync();
}
