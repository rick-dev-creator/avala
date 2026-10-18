using System.Text.Json;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Canvases;
using Avala.Canvas.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Canvas.Showing;

internal sealed class MediaDesk(IJobAttachments attachments, IAgents agents, IEventBus bus)
    : IHandle<SessionOpened>, IHandle<AgentActivity>
{
    private readonly Dictionary<SessionId, string> folders = [];

    public ValueTask HandleAsync(SessionOpened integrationEvent, CancellationToken cancellationToken)
    {
        folders[integrationEvent.Session] = integrationEvent.WorkingDirectory;

        return ValueTask.CompletedTask;
    }

    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Event is not ToolCalled { Tool: ShowMediaTool.Name } called)
        {
            return;
        }

        var request = MediaRequest.Of(called.Input);
        var path = request.Match(asked => Located(called.Session, asked.Path), _ => string.Empty);
        var kept = await request.Match(
            _ => KeepAsync(path, cancellationToken),
            refusal => ValueTask.FromResult<Result<Attachment, CanvasError>>(refusal));
        var answer = await kept.Match(
            async media =>
            {
                await bus.PublishAsync(new MediaShown(called.Session, called.Turn, called.Item, media, request.Match(asked => asked.Caption, _ => string.Empty)), cancellationToken);

                return new ToolResult(called.Item, $"Shown to the person in the conversation as {media.Name}.");
            },
            refusal => Task.FromResult(new ToolResult(called.Item, $"Not shown: {Reason(refusal, path)}") { IsError = true }));
        _ = await agents.ReturnAsync(called.Session, answer, cancellationToken);
    }

    private static string Reason(CanvasError refusal, string path) => refusal switch
    {
        CanvasError.NotAnImage => $"{Path.GetFileName(path)} is not an image Avala shows; give a PNG, JPEG, GIF or WebP file.",
        CanvasError.MediaTooLarge => $"{Path.GetFileName(path)} is larger than Avala keeps; show a smaller image.",
        CanvasError.MissingMedia => $"{path} was not found or cannot be read.",
        CanvasError.MediaNotKept => "Avala could not keep a copy of it.",
        _ => "the input needs a path to an image file.",
    };

    private string Located(SessionId session, string path) =>
        Path.IsPathRooted(path) || !folders.TryGetValue(session, out var folder) ? path : Path.GetFullPath(Path.Combine(folder, path));

    private async ValueTask<Result<Attachment, CanvasError>> KeepAsync(string path, CancellationToken cancellationToken) =>
        !attachments.MediaTypeOf(path).StartsWith("image/", StringComparison.Ordinal)
            ? CanvasError.NotAnImage
            : (await attachments.KeepFileAsync(path, cancellationToken)).Match<Result<Attachment, CanvasError>>(
                media => media,
                refusal => refusal switch
                {
                    AttachmentRefusal.TooLarge => CanvasError.MediaTooLarge,
                    AttachmentRefusal.Unwritable => CanvasError.MediaNotKept,
                    _ => CanvasError.MissingMedia,
                });

    private sealed record MediaRequest(string Path, string Caption)
    {
        public static Result<MediaRequest, CanvasError> Of(string input)
        {
            try
            {
                using var document = JsonDocument.Parse(input);

                return document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("path", out var path)
                    && path.ValueKind == JsonValueKind.String
                    && path.GetString() is { Length: > 0 } given
                        ? new MediaRequest(given.Trim(), CaptionOf(document.RootElement))
                        : CanvasError.MalformedMedia;
            }
            catch (JsonException)
            {
                return CanvasError.MalformedMedia;
            }
        }

        private static string CaptionOf(JsonElement root) =>
            root.TryGetProperty("caption", out var caption) && caption.ValueKind == JsonValueKind.String ? caption.GetString()!.Trim() : string.Empty;
    }
}
