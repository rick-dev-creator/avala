using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Recording.Recordings;
using Avala.Sdk;

namespace Avala.Recording.Storage;

internal sealed class RecordingFormat
{
    public const string Name = "avala-recording";

    public const int Version = 1;

    private static readonly JsonWriterOptions Options = new() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly JsonSerializerOptions ComponentData = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new OptionalTextConverter() },
    };

    private readonly Dictionary<TurnId, int> turns = [];
    private readonly Utf8JsonWriter json;
    private readonly Redaction redaction;

    private RecordingFormat(Utf8JsonWriter json, Redaction redaction)
    {
        this.json = json;
        this.redaction = redaction;
    }

    public static byte[] Write(SessionRecording recording)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var json = new Utf8JsonWriter(buffer, Options))
        {
            new RecordingFormat(json, recording.Redaction).Recording(recording);
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static string Enum<TEnum>(TEnum value)
        where TEnum : struct, System.Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private void Recording(SessionRecording recording)
    {
        var header = recording.Header;
        json.WriteStartObject();
        json.WriteString("format", Name);
        json.WriteNumber("version", Version);
        json.WriteString("recordedAt", header.RecordedAt);
        Object("provider", () =>
        {
            json.WriteString("id", header.Provider.Id);
            json.WriteString("name", header.Provider.Name);
        });
        Capabilities(header.Capabilities);
        Present(header.Account, account => Object("account", () =>
        {
            Text("id", account.Id);
            Text("label", account.Label);
        }));
        Object("options", () =>
        {
            json.WriteString("permissions", Enum(header.Options.Permissions));
            json.WriteBoolean("resumed", header.Options.Resume.IsSome);
            Optional("model", header.Options.Model.Model);
            Optional("effort", header.Options.Model.Effort);
            Array("tools", header.Options.Tools, tool => Object(() =>
            {
                json.WriteString("name", tool.Name);
                json.WriteString("surface", Enum(tool.Surface));
            }));
        });
        Array("entries", recording.Entries, Entry);
        json.WriteEndObject();
    }

    private void Capabilities(CapabilitySet declared) => Object("capabilities", () =>
    {
        foreach (var component in declared.Components)
        {
            json.WritePropertyName(JsonNamingPolicy.CamelCase.ConvertName(component.GetType().Name));
            Data(JsonSerializer.SerializeToElement(component, component.GetType(), ComponentData));
        }
    });

    private void Data(JsonElement data)
    {
        switch (data.ValueKind)
        {
            case JsonValueKind.Object:
                Object(() =>
                {
                    foreach (var property in data.EnumerateObject())
                    {
                        json.WritePropertyName(property.Name);
                        Data(property.Value);
                    }
                });
                break;
            case JsonValueKind.Array:
                json.WriteStartArray();

                foreach (var item in data.EnumerateArray())
                {
                    Data(item);
                }

                json.WriteEndArray();
                break;
            case JsonValueKind.String:
                json.WriteStringValue(redaction.Apply(data.GetString()!));
                break;
            default:
                data.WriteTo(json);
                break;
        }
    }

    private void Entry(RecordedEntry entry) => Object(() =>
    {
        json.WriteNumber("at", (long)entry.At.TotalMilliseconds);

        switch (entry.Fact)
        {
            case Observed observed:
                Object("event", () => Event(observed.Event));
                break;
            case FileCaptured file:
                Object("file", () =>
                {
                    Item(file.Item);
                    Text("path", file.Path);
                    Text("content", file.Content);
                });
                break;
            case Sent sent:
                Object("send", () => Send(sent.Turn));
                break;
            case Responded responded:
                Object("respond", () => Decision(responded.Decision));
                break;
            case Answered answered:
                Object("answer", () => Answer(answered.Answer));
                break;
            case Returned returned:
                Object("return", () => Result(returned.Result));
                break;
            case Interrupted:
                Object("interrupt", () => { });
                break;
            case StreamEnded ended:
                Object("end", () => json.WriteBoolean("crashed", ended.Crashed));
                break;
            case Stopped:
                Object("stop", () => { });
                break;
        }

        Present(entry.Refusal, error => json.WriteString("refused", Enum(error)));
    });

    private void Event(IAgentEvent recorded)
    {
        json.WriteString("type", JsonNamingPolicy.CamelCase.ConvertName(recorded.GetType().Name));
        json.WriteNumber("turn", Turn(recorded.Turn));
        ItemEvent(recorded);
        InteractionEvent(recorded);
        TurnEvent(recorded);
    }

    private void ItemEvent(IAgentEvent recorded)
    {
        switch (recorded)
        {
            case ItemStarted started:
                Item(started.Item);
                json.WriteString("kind", Enum(started.Kind));
                Text("title", started.Title);
                Optional("input", started.Input);
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
            case ItemCompleted completed:
                Item(completed.Item);
                json.WriteString("outcome", Enum(completed.Outcome));
                break;
            case ToolCalled called:
                Item(called.Item);
                json.WriteString("tool", called.Tool);
                Text("input", called.Input);
                break;
            case ToolReturned returned:
                Item(returned.Item);
                Object("result", () => Result(returned.Result));
                break;
        }
    }

    private void InteractionEvent(IAgentEvent recorded)
    {
        switch (recorded)
        {
            case PermissionRequested requested:
                Item(requested.Item);
                Text("title", requested.Title);
                json.WriteString("kind", Enum(requested.Kind));
                Text("target", requested.Target);
                break;
            case PermissionResolved resolved:
                Item(resolved.Item);
                json.WriteString("answer", Enum(resolved.Answer));
                break;
            case FormRequested form:
                Item(form.Item);
                Form(form.Form);
                break;
            case FormAnswered answered:
                Item(answered.Item);
                Object("answer", () => Answer(answered.Answer));
                break;
            case RequestWithdrawn withdrawn:
                Item(withdrawn.Item);
                break;
        }
    }

    private void TurnEvent(IAgentEvent recorded)
    {
        switch (recorded)
        {
            case PlanUpdated plan:
                Array("steps", plan.Steps, step => Object(() =>
                {
                    Text("title", step.Title);
                    json.WriteString("status", Enum(step.Status));
                }));
                break;
            case UsageReported usage:
                Usage(usage);
                break;
            case LimitReported limit:
                Object("limit", () =>
                {
                    json.WriteString("window", limit.Limit.Window);
                    json.WriteNumber("usedFraction", limit.Limit.UsedFraction);
                    Present(limit.Limit.ResetsAt, resets => json.WriteString("resetsAt", resets));
                });
                break;
            case MessageQueued queued:
                Text("text", queued.Text);
                break;
            case ModelReported model:
                Text("model", model.Model);
                Optional("effort", model.Effort);
                break;
            case ResumeTokenIssued issued:
                Text("token", issued.Token.Value);
                break;
            case TurnCompleted completed:
                json.WriteString("outcome", Enum(completed.Outcome));
                break;
        }
    }

    private void Usage(UsageReported usage)
    {
        Object("tokens", () =>
        {
            json.WriteNumber("input", usage.Tokens.Input);
            json.WriteNumber("output", usage.Tokens.Output);
            json.WriteNumber("cacheRead", usage.Tokens.CacheRead);
            json.WriteNumber("cacheWrite", usage.Tokens.CacheWrite);
            json.WriteNumber("reasoning", usage.Tokens.Reasoning);
        });
        Present(usage.Cost, cost => Object("cost", () =>
        {
            json.WriteNumber("amount", cost.Amount);
            json.WriteString("currency", cost.Currency);
        }));
    }

    private void Form(AgentForm form) => Object("form", () =>
    {
        json.WriteString("purpose", Enum(form.Purpose));
        Text("title", form.Title);
        Text("context", form.Context);
        Array("fields", form.Fields, field => Object(() =>
        {
            json.WriteString("id", field.Id);
            Text("header", field.Header);
            Text("prompt", field.Prompt);
            json.WriteString("kind", Enum(field.Kind));
            Array("options", field.Options, option => Object(() =>
            {
                Text("label", option.Label);
                Text("description", option.Description);
                json.WriteBoolean("recommended", option.Recommended);
            }));
            json.WriteBoolean("acceptsFreeText", field.AcceptsFreeText);
        }));
    });

    private void Send(UserTurn turn)
    {
        Text("text", turn.Text);

        if (turn.MidTurn)
        {
            json.WriteBoolean("midTurn", true);
        }
    }

    private void Decision(PermissionDecision decision)
    {
        Item(decision.Item);
        json.WriteString("answer", Enum(decision.Answer));
        Optional("message", decision.Message);
    }

    private void Answer(FormAnswer answer)
    {
        Item(answer.Item);
        Array("fields", answer.Fields, field => Object(() =>
        {
            json.WriteString("field", field.Field);
            Array("chosen", field.Chosen, chosen => json.WriteStringValue(redaction.Apply(chosen)));
            Optional("text", field.Text);
            json.WriteBoolean("confirmed", field.Confirmed);
        }));
        json.WriteBoolean("declined", answer.Declined);
        Optional("message", answer.Message);
    }

    private void Result(ToolResult result)
    {
        Item(result.Item);
        Text("content", result.Content);
        json.WriteBoolean("isError", result.IsError);
    }

    private int Turn(TurnId turn)
    {
        if (!turns.TryGetValue(turn, out var ordinal))
        {
            ordinal = turns.Count + 1;
            turns[turn] = ordinal;
        }

        return ordinal;
    }

    private void Item(ItemId item) => json.WriteString("item", item.Value);

    private void Text(string name, string value) => json.WriteString(name, redaction.Apply(value));

    private static void Present<T>(Option<T> value, Action<T> write)
        where T : notnull
    {
        foreach (var present in value.Match<T[]>(given => [given], () => []))
        {
            write(present);
        }
    }

    private void Optional(string name, Option<string> value) => Present(value, present => Text(name, present));

    private void Object(Action write)
    {
        json.WriteStartObject();
        write();
        json.WriteEndObject();
    }

    private void Object(string name, Action write)
    {
        json.WritePropertyName(name);
        Object(write);
    }

    private void Array<T>(string name, IEnumerable<T> items, Action<T> write)
    {
        json.WriteStartArray(name);

        foreach (var item in items)
        {
            write(item);
        }

        json.WriteEndArray();
    }
}
