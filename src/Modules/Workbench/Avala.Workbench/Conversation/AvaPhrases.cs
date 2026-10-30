using Avala.Components.Status;

namespace Avala.Workbench.Conversation;

internal static class AvaPhrases
{
    public static string Toggle(bool open) => open ? "Fold Ava" : "Open Ava";

    public static string Kanji(Mood mood) => mood switch
    {
        Mood.Asking => "質問",
        Mood.Worried => "心配",
        Mood.Pleased => "満足",
        _ => "集中",
    };

    public static string Moment(StatusKind status) => status switch
    {
        StatusKind.Checking => "checking",
        StatusKind.NeedsYou => "needs you",
        StatusKind.Held => "held",
        StatusKind.ReadyForReview => "ready for review",
        StatusKind.Done => "done",
        StatusKind.Failed => "failed",
        _ => "working",
    };

    public static string Line(StatusKind status) => status switch
    {
        StatusKind.Checking => "Running your checks now. Let's see if they like it.",
        StatusKind.NeedsYou => "I need your answer before I go on.",
        StatusKind.Held => "I've stopped here. Tell me how you want to go on.",
        StatusKind.ReadyForReview => "It's ready for you. Take a look whenever you want.",
        StatusKind.Done => "Done. That was a good one.",
        StatusKind.Failed => "Something broke. I'll show you where.",
        _ => "On it. Stay close, this part is the fun one.",
    };
}
