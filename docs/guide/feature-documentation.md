# Feature documentation

Let each job document the feature it builds: the plan it proposed, every change you asked for and how it ended, as Markdown with a Mermaid C4 diagram in your repository, played in Avala as an animated C4.

## What it does

- **A version per moment that matters.** When a job's harness has proposed its plan, once a step starts or the turn ends, a documentation companion, a small job on the repository's `research` role, writes **v1 Proposed**: the feature in Markdown with a Mermaid C4 diagram and one section per element. A new plan writes a **Deviation**. When the job is ready for review, Avala writes **As built** itself, from the last version and the job's evidence: its checks and the files it changed. The working agent never sees the companion, so its context stays on the work.
- **Every change you ask for is recorded.** A steer, a send back or a message that starts a new attempt is a change request, with its text, time and attempt. The next version explains it and links it, and `changes.md` lists them.
- **Committed in the job's branch**, under the docs folder, one folder per feature:

  ```
  docs/features/<feature>/
    README.md        the feature's versions in order
    plans/vN.md      each plan as the harness proposed it
    vN-<kind>.md     each version, with its Mermaid C4
    changes.md       every change request and the version it produced
    toc.yml          for the docs site
  ```

  A later job on the same feature continues its numbering.
- **Read in Avala.** A project's sidebar lists its documented features under **Docs**; choosing one opens its page with its versions, its document and the C4 player. Inside a job, the **Docs** glyph in the header opens a pane with this job's feature, its player, its history and the repository's other docs. The conversation shows a quiet line for each version written and each change request recorded.
- **The C4 player.** Playing a version builds it from the one before: new elements appear, changed ones are highlighted, removed ones fade. A `C4Dynamic` flow plays hop by hop, numbered, each element lighting as it is reached. Each step brings its section of the document into view, and choosing a section moves the diagram to its step. **Back**, **Play**, **Next**, the speed and **Back to the start** sit under the diagram. Mermaid the player cannot read shows as Mermaid draws it.
- **The connected history.** The Docs pane lists the job's plans in order with the change requests between them. Choosing one shows what happened, why it deviated, what changed and its records, and plays its C4. A version written by a companion offers **Writer's conversation**, which opens the companion's job. Companions stay out of the sidebar and the delegation pane; their cost counts in the usage like any job's.

## How to use it

1. **Turn it on** in Settings, Repositories, **Feature documentation**, or add the `docs` section to `.avala/jobs.json`. Jobs read the file from the commit they start from, so the switch shows what your working copy says and, until it is committed, an amber dot and **Commit .avala/jobs.json**, which commits that file alone on the checked-out branch and leaves anything else you staged as it was.
2. **Start a job** from New job. The feature chip under the instruction holds the feature Avala suggests from it; open it to choose another of the repository's features, a new one, or **Don't document this job**.
3. **Follow it** in the job's Docs pane, and read the feature later from the project's **Docs** in the sidebar.

## The docs site

**Create site**, in the same Settings section, lays your docs out the way Microsoft Learn structures documentation, with DocFX, each page with its article type in `ms.topic`:

```
docs/
  docfx.json  toc.yml
  index.md                    overview
  get-started/quickstart.md   quickstart
  concepts/architecture.md    the C4 context and containers, as built
  concepts/glossary.md
  how-to/  tutorials/
  reference/configuration.md
  features/<feature>/         the versioned feature docs
  architecture/decisions/     one record per significant decision
  whats-new.md
  media/  includes/
  template/public/            Avala's C4 player for the site
```

It writes nothing in your working copy: it starts a job, whose worktree Avala lays out before its first turn, adding only the files the repository lacks, and the job fills the overview, quickstart, architecture and configuration from the repository, records the decisions of features already documented and lists their As built versions in `whats-new.md`. The site is delivered like any job, so it reaches your docs folder once you merge it. Until then Settings reads "Being prepared by its job", with **Open its job**; a discarded site job offers Create site again. From then on, a deviation that shapes the structure, a key quality attribute or something hard to reverse adds an architecture decision record, linked both ways with its version, and each As built adds its entry to `whats-new.md`.

On the site each C4 diagram is Avala's player, `<avala-c4>`: the diagram laid out the way Avala lays it out, in the site's light or dark theme, with **Previous**, **Play**, **Next** and the speed. Playing it reveals the elements level by level, then walks each relation, a flow hop by hop with its number. Choosing an element scrolls to its section. Other Mermaid diagrams show as DocFX's Mermaid draws them.

**Open the docs site** with the globe in a job's Docs pane: Avala runs `docfx` on the job's leased port and shows the site in the Preview pane. It needs DocFX: `dotnet tool install -g docfx`; without it, Avala says so instead of offering it.

## Settings and files

**`.avala/jobs.json`**, `docs` section:

```json
{ "docs": { "enabled": true, "folder": "docs/features" } }
```

- `enabled`: the one switch; off when the section is missing.
- `folder`: where the features live, `docs/features` by default; a relative path inside the repository.

The documentation companion runs on the `research` role of the `delegation` section, so `{ "delegation": { "roles": { "research": { "model": "haiku", "effort": "low" } } } }` writes the docs with a cheaper model.

## Limits

- A version for code the diff touches outside the plan is not written yet; only a new plan or an attempt that changes it writes a deviation.
- The site's player walks one version at a time; building a version from the one before, as the app does, is only in Avala.
