# Keep notes as folders in the job's branch, committed by Avala and guarded from the agent

## Status

Proposed.

## Context

A developer wants to take notes while they watch a job work: ideas, plans and bugs they notice. They want the history of notes kept in their own repository, grouped by job, and they want to turn notes into work Avala can run. The earlier version of the feature left open where notes live and what a task is. It noted that the repository's `.avala` folder holds rules an agent cannot edit, and that tasks might feed Autopilot's backlog in `.avala/backlog.json`. A note also has to carry the images the job's agent shows, which the plan adds through `show_media`.

## Decision

A new Notes module owns the Note aggregate and its lifecycle. Each note is stored as a folder with a `note.md` and its media, referenced by relative path, in the job's branch. Avala commits notes itself between the agent's turns, and it guards the notes folder from the agent's edits. A discarded job's notes are kept, and a note taken outside a job is still committed. A note turns into an idea that carries its media. An idea starts a job through `IJobs.SubmitAsync` that asks its questions first, or it is published as a forge issue. Ideas do not go through Autopilot's backlog.

## Alternatives

- Keep notes in Avala's own state outside the repository. This was rejected because the request asks for the history to be kept in the developer's repository.
- Let the agent write notes like any other file. This was rejected because notes are the developer's, and the plan guards their folder from the agent's edits.
- Make tasks entries in Autopilot's backlog. The earlier version considered it. The plan instead keeps ideas separate and starts jobs or issues from them directly.

## Consequences

- Notes travel with the code and are reviewed, merged and kept like the job's other commits.
- The format of a note's folder lands in developers' repositories, so changing it later means migrating notes that are already committed.
- Avala now adds commits of its own to a job's branch between turns, and the guard has to keep the agent out of the notes folder.
- Discarding a job no longer discards everything on its branch: Notes has to keep that job's notes.
- Ideas depend on Jobs and Forges, not on Autopilot.

## Versions it shaped

- [Necesito un nuevo feature avala v2](../../features/necesito-un-nuevo-feature-avala/v2-deviation.md)
