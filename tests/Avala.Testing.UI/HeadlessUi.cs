using Avalonia.Headless;

namespace Avala.Testing.UI;

public sealed class HeadlessUi
{
    private readonly HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(HeadlessApp));

    public Task RunAsync(Action script, CancellationToken cancellationToken) =>
        session.Dispatch(
            () =>
            {
                try
                {
                    script();
                }
                finally
                {
                    ViewScript.CloseAll();
                }
            },
            cancellationToken);

    public Task RunAsync(Func<Task> script, CancellationToken cancellationToken) =>
        session.Dispatch(
            async () =>
            {
                try
                {
                    await script();
                }
                finally
                {
                    ViewScript.CloseAll();
                }

                return true;
            },
            cancellationToken);
}
