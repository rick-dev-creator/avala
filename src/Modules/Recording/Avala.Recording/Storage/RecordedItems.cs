using System.Text.Json;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Recording.Recordings;
using Avala.Sdk;

namespace Avala.Recording.Storage;

internal sealed class RecordedItems(Utf8JsonWriter json, Redaction redaction)
{
    public void Write(IAgentEvent recorded)
    {
        switch (recorded)
        {
            case ItemStarted started:
                Item(started.Item);
                json.WriteString("kind", Enum(started.Kind));
                Text("title", started.Title);
                Optional("input", started.Input);
                Optional("parent", started.Parent.Map(parent => parent.Value));
                break;
            case ItemDrafted drafted:
                Item(drafted.Item);
                json.WriteString("kind", Enum(drafted.Kind));
                Text("title", drafted.Title);
                json.WriteNumber("received", drafted.Received);
                break;
            case CanvasStarted canvas:
                Item(canvas.Item);
                Text("title", canvas.Title);
                json.WriteString("mediaType", canvas.MediaType);
                break;
            case ItemProgressed progressed:
                Item(progressed.Item);
                Text("text", progressed.Text);
                break;
            case ItemTokensReported tokens:
                Item(tokens.Item);
                json.WriteNumber("tokens", tokens.Tokens);
                break;
            case ItemCompleted completed:
                Item(completed.Item);
                json.WriteString("outcome", Enum(completed.Outcome));
                break;
        }
    }

    private static string Enum<TEnum>(TEnum value)
        where TEnum : struct, System.Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private void Item(ItemId item) => json.WriteString("item", item.Value);

    private void Text(string name, string value) => json.WriteString(name, redaction.Apply(value));

    private void Optional(string name, Option<string> value)
    {
        foreach (var present in value.Match<string[]>(given => [given], () => []))
        {
            Text(name, present);
        }
    }
}
