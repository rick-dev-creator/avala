using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Workbench.Board;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Mentioning;

internal sealed class OtherMentions(JobBoard board, INotes notes)
{
    public const int ReplyBrief = 400;

    public const int TitleLength = 80;

    private static Mention Noted(NoteRecord note)
    {
        var images = note.Media.Select(media => Path.Combine(note.Root, note.Folder, media.File)).ToList();
        var attached = images.Count == 0 ? string.Empty : $"\nIts images: {string.Join(", ", images)}";

        return new Mention(MentionKind.Note, note.Title, note.Title, note.Place.Job.Match(_ => "a job's note", () => "a project note"), $"the note \"{note.Title}\": {note.Body}{attached}");
    }

    public async Task<IEnumerable<Mention>> NotesAsync(string query, CancellationToken cancellationToken) =>
        query.Length == 0 ? [] : (await notes.SearchAsync(query, cancellationToken)).Select(Noted);

    public IEnumerable<Mention> Jobs(JobId current) =>
        board.Jobs.Values
            .Where(other => other.Job != current)
            .OrderByDescending(other => other.Summary.Submitted)
            .Select(other => new Mention(MentionKind.Job, Title(other.Summary.Instruction), Title(other.Summary.Instruction), string.Empty, Brief(other)));

    private static string Brief(BoardJob job)
    {
        var reply = job.Transcript.Entries.OfType<MessageEntry>().LastOrDefault()?.Text ?? string.Empty;
        var said = reply.Length <= ReplyBrief ? reply : $"{reply[..ReplyBrief]}…";

        return said.Length == 0
            ? $"another job, \"{Title(job.Summary.Instruction)}\", {job.Status}: {job.Summary.Instruction}"
            : $"another job, \"{Title(job.Summary.Instruction)}\", {job.Status}: {job.Summary.Instruction}\nIts last reply: {said}";
    }

    private static string Title(string instruction)
    {
        var line = instruction.Trim().Split('\n', 2)[0].Trim();

        return line.Length <= TitleLength ? line : $"{line[..(TitleLength - 1)].TrimEnd()}…";
    }
}
