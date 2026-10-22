# Notes and ideas

Write down what you notice while a job works, keep what the agent shows you, and turn a note into an idea that starts a job.

## What it does

- **Visual evidence.** When a job builds or changes something visual, its agent shows you a screenshot or an image in the conversation. Avala keeps its own copy, so the file need not stay in the repository.
- **A viewer.** Opening an image shows it large, and the viewer steps through every image the job has shown.
- **A menu on what you see.** An image, a link or selected text offers Copy, Copy as Markdown, Copy link, Open and Add to note. You can also drag them out of the conversation into other apps.
- **Notes.** A note is a title, a body and the images it refers to. Notes are kept with the job they came from, and a note can also belong to a project instead of a job.
- **Your repository keeps them.** Each note is a folder with a `note.md` and its images, written to your repository and committed by Avala, so the history of your notes travels with the code, grouped by job.
- **Search.** One search covers the notes of every job and project, and `@` mentions a note in a prompt.
- **Ideas.** A note becomes an idea that carries its images. A job can start from an idea and ask its questions first, or the idea can be published as an issue.

## How to use it

1. **Look at the evidence.** An image the agent shows appears in the conversation. Click it to open the viewer; ← and → step through the job's images and Esc closes it.
2. **Copy or drag it.** Right-click an image for **Open**, **Copy**, **Copy as Markdown**, **Copy link** and **Add to note**. Drag an image out to hand over its file. A link such as a CI log can be dragged out too. To drag selected text, hold Alt while you drag: without Alt the drag extends the selection.
3. **Add it to a note.** **Add to note** puts the image in the job's latest note, or in a new note named after the image's caption when the job has none yet. A line under the image says what happened.
4. **Open the Notes pane.** The Notes button beside the job opens the job's notes. For a project's notes, right-click the project in the sidebar and choose **Notes**.
5. **Write a note.** Use the pane's **New note** button, give it a title and a body, and choose **Save**. Open a note from the list to edit it, and **Archive** removes it from the list.
6. **Search.** Type in the pane's search box to look in the notes of every job and project. Clear it to return to this job's notes.
7. **Mention a note.** Type `@` and a few letters in the composer. A matching note appears with the files, folders, jobs and commits; when you send, the message tells the agent the note's text and the paths of its images.
8. **Make an idea.** Open a saved note and choose **Make idea**. The note keeps its text and images, and its row in the list is marked **Idea**.
9. **Publish an idea as an issue.** Open the idea and choose **Publish issue**. A line under the note gives the issue's address.
10. **Start a job from an idea.** Open the idea and choose **Start job**. The job starts in the idea's repository with the idea's text and images, and its agent first asks you every question it needs answered, then works.

## Settings and files

- **`.avala/notes/`** in the repository holds the notes. A job's notes are in `.avala/notes/<job id>/`, a project's in `.avala/notes/project/`.
- **A note's folder** is named by its time and title, such as `2026-10-19-1432-login-bug/`. It holds `note.md` and the images, which the note refers to by relative path:

```markdown
---
id: 0197c0f2-6a1e-7c2b-9d3f-5b1c2a4e8f10
state: idea
job: 0197c0f1-0d52-7a10-b3a4-6e7f8091a2b3
created: 2026-10-19T14:32:00.0000000+00:00
updated: 2026-10-19T14:40:00.0000000+00:00
---

# Login bug

The button overlaps the logo.

![The login screen](login.png)
```

- **`state`** is `kept`, `idea` or `archived`.

## Good to know

- Avala commits notes itself. A note written while the agent is working waits and is committed when the turn ends, so the agent's own commits stay its own. A note taken outside a job is committed to the project.
- The agent cannot edit the notes folder: the built-in guard `notes-belong-to-the-developer` denies it.
- When you discard a job, or it fails, its notes are copied into your repository's own `.avala/notes/<job id>/` and committed there.
- An image in a note is a PNG, JPEG, GIF or WebP of at most 20 MB.
- An issue is opened on the forge your repository's `origin` points to, through the account you connected in Settings. A repository without a known forge, or a forge that cannot open issues, refuses it and says so. The issue carries the idea's title and text, and the names of its images, which are not uploaded.
- Markdown links in a reply cannot be dragged out yet; a CI log link can.

## See also

- [Jobs and conversations](jobs-and-conversations.md)
- [Canvas and previews](canvas-and-previews.md)
- [Permissions and autonomy](permissions-and-autonomy.md)
