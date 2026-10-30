namespace Avala.Components.Status;

public static class Moods
{
    public static Mood Of(StatusKind status) => status switch
    {
        StatusKind.NeedsYou or StatusKind.Held => Mood.Asking,
        StatusKind.Failed => Mood.Worried,
        StatusKind.ReadyForReview or StatusKind.Done => Mood.Pleased,
        _ => Mood.Focused,
    };
}
