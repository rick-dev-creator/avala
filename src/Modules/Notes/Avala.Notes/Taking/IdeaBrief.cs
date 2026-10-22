using Avala.Agents.Contracts.Sessions;
using Avala.Forges.Contracts;
using Avala.Notes.Contracts;

namespace Avala.Notes.Taking;

internal static class IdeaBrief
{
    public const string Questions = "Before you change anything, ask me every question you need answered to do this well, using the AskUserQuestion tool, and wait for my answers. Only then do the work.";

    public static string Instruction(NoteRecord idea) =>
        $"{Questions}\n\nThe idea: {idea.Title}\n\n{idea.Body}".TrimEnd();

    public static IssueDraft Issue(NoteRecord idea)
    {
        var images = idea.Media.Count == 0 ? string.Empty : $"\n\nImages kept with the note: {string.Join(", ", idea.Media.Select(media => media.File))}";

        return new IssueDraft(idea.Title, $"{idea.Body}{images}\n\nPublished by Avala from an idea.".TrimStart());
    }

    public static IReadOnlyList<Attachment> Attachments(NoteRecord idea) =>
        [.. idea.Media.Select(media => new Attachment(media.File, media.MediaType, Path.Combine(idea.Root, idea.Folder, media.File)))];
}
