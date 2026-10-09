using Avala.Sdk;
using Avala.Sdk.Updates;

namespace Avala.Workbench.Tests.Updates;

internal sealed class FakeUpdates : IUpdates
{
    public static AvailableUpdate Newer { get; } = new("0.2.0", new Uri("https://github.com/rick-dev-creator/avala/releases/tag/v0.2.0"));

    public UpdateState Latest { get; set; } = UpdateState.NotChecked;

    public UpdateState Answer { get; init; } = new(UpdateStatus.Available, Newer);

    public int Checks { get; private set; }

    public static UpdateState Found => new(UpdateStatus.Available, Newer);

    public static UpdateState Of(UpdateStatus status) => new(status, Option<AvailableUpdate>.None);

    public Task<UpdateState> CheckAsync(CancellationToken cancellationToken)
    {
        Checks++;
        Latest = Answer;

        return Task.FromResult(Answer);
    }
}
