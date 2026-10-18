using Avala.Agents.Contracts.Events;
using Avala.Canvas.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Cards;
using Avala.Workbench.Conversation;
using Avala.Workbench.Replies;
using Avala.Workbench.Tests.Cards;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class TimelineItemsTests
{
    private static readonly Asking Asked = new();

    public static TheoryData<string, Type> EveryEntry => new()
    {
        { "prompt", typeof(PromptViewModel) },
        { "message", typeof(MessageViewModel) },
        { "reasoning", typeof(ReasoningViewModel) },
        { "tool", typeof(ToolViewModel) },
        { "plan", typeof(PlanViewModel) },
        { "canvas", typeof(CanvasViewModel) },
        { "media", typeof(MediaViewModel) },
        { "permission", typeof(PermissionCardViewModel) },
        { "denied", typeof(DeniedCardViewModel) },
        { "form", typeof(FormCardViewModel) },
        { "turn", typeof(TurnEndViewModel) },
        { "restart", typeof(RestartViewModel) },
        { "ci", typeof(CiViewModel) },
    };

    [Theory]
    [MemberData(nameof(EveryEntry))]
    public void EachKindOfEntryGetsItsOwnViewModel(string kind, Type item) =>
        Assert.IsType(item, new TimelineItems(Replying.With(new FakePermissionAnswers()), new FakeClipboard(), Tools.Viewing()).Create(JobId.New(), Entry(kind)));

    private static ITimelineEntry Entry(string kind) => kind switch
    {
        "prompt" => new PromptEntry("p", 1, AttemptOrigin.Initial, "Fix JPY rounding", Option<AttemptOutcome>.None),
        "message" => new MessageEntry("m", "Done", ItemOutcome.Succeeded),
        "reasoning" => new ReasoningEntry("r", "Hmm", DateTimeOffset.UnixEpoch, Option<TimeSpan>.None, Option<ItemOutcome>.None),
        "tool" => new ToolEntry("t", ItemKind.Search, "rg Math.round", string.Empty, Option<ItemOutcome>.None),
        "plan" => new PlanEntry("plan", []),
        "canvas" => new CanvasEntry("c", "Flow", "text/vnd.mermaid", string.Empty, CanvasStatus.Streaming),
        "media" => new MediaEntry("media", new Avala.Agents.Contracts.Sessions.Attachment("screen.png", "image/png", "/data/screen.png"), "The checkout page"),
        "permission" => Asked.Permission(),
        "denied" => new Denying().Entry(),
        "form" => Asked.Form(),
        "turn" => new TurnEndEntry("turn", TurnOutcome.Finished, TimeSpan.FromSeconds(3), default, []),
        "ci" => new CiEntry(EntryKeys.Ci(12, "4f2a9c1"), 12, "4f2a9c1", []),
        _ => new RestartEntry(EntryKeys.Restart, Kept: true),
    };
}
