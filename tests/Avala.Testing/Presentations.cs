using Avala.Sdk.Presentation;

namespace Avala.Testing;

public static class Presentations
{
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(30);

    extension(IPresentation component)
    {
        public async Task<Presented> PresentsAfterAsync(Action act, Func<string> describe, CancellationToken cancellationToken)
        {
            var presented = new TaskCompletionSource<Presented>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnPresented(object? sender, Presented signal) => presented.TrySetResult(signal);
            var before = component.Revision;
            component.Presented += OnPresented;

            try
            {
                act();

                return await presented.Task.WaitAsync(HangGuard, cancellationToken);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException(
                    $"{component.GetType().Name} did not present within {HangGuard.TotalSeconds:0} s after revision {before}. Last state: {describe()}");
            }
            finally
            {
                component.Presented -= OnPresented;
            }
        }
    }
}
