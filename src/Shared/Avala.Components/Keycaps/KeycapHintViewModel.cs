namespace Avala.Components.Keycaps;

public sealed class KeycapHintViewModel(string keys, string action) : IKeycapHintViewModel
{
    public string Keys { get; } = keys;

    public string Action { get; } = action;
}
