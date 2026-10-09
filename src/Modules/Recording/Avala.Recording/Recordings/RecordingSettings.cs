namespace Avala.Recording.Recordings;

internal sealed record RecordingSettings(bool Enabled, IReadOnlyList<string> Redactions)
{
    public static RecordingSettings Off { get; } = new(false, []);
}
