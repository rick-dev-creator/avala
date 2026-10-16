# Delegation and orchestration

An agent can hand self-contained work to sub-agents, which run as child jobs governed and verified like any other.

## What it does

- **Avala offers the agent a `delegate` tool** when the repository allows delegation. Each call starts a child job in a worktree made from the parent's current work, on a connection Avala chooses; several calls run in parallel.
- **A child is a job like any other**: its own session, permissions, checks and budget. When it ends, its verified work is merged into the parent's worktree, and the parent receives its summary, the files it changed and its evidence.
- **Roles.** A `worker`, the default, changes files. A `reviewer` or `research` child only reads and reports back without changing the parent's worktree.
- **Children ask their parent first**, when the repository says so: the parent answers through `answer_child`, and allows only what it would be allowed to do itself; anything else goes to you.
- **A child never runs looser than its parent**, and its budget is carved out of its parent's.

## How to use it

1. **Allow delegation** in the `delegation` section of `.avala/jobs.json` and commit it.
2. **Ask the agent to split the work.** It decides when to call `delegate`.
3. **Follow the tree beside the conversation.** In the header of a job that delegated, or of one of its children, the tree glyph opens the **Delegation** pane. The job you started is at the top with how much context it has left (marked "compacted" when its harness compacted it), the tokens of the whole tree and how many of its jobs need you, are running, are done or were refused. Each child below is one row: what it does now, the context it has left and what it spent, and how many sub-agents it runs. Children that need you come first, then running ones; finished ones fold into one "N done" line. Click a row to open its card, one at a time: its harness and connection, its last three actions and how long ago they started, its spend against its carve and its tokens, and its harness's own sub-agents as dashed pills with the command each runs now, how many tools it has used and how many tokens its conversation holds. **Open conversation** opens the job. Sub-agents' tools stay out of the conversation, which keeps one row per sub-agent; a permission one of them asks for still shows there. When a job you are viewing starts a sub-agent, the pane opens by itself, also for a job that delegated nothing; once you close it, it opens again only for the next sub-agent. The footer counts the jobs on each connection. The pane takes the preview's place beside the conversation, keeps its width and hides in focus mode.
4. **See every tree** on **Overview**, **Delegation**, which shows every job that delegated, the budget carved for each child, their sub-agents and the delegations that were refused. The job inspector's **Delegation** section lists them too.
5. **Answer a child that asks** from the card "Sub-agent … is waiting for this job" when it comes to you.

## Settings and files

**`.avala/jobs.json`**, `delegation` section:

```json
{ "delegation": { "connections": ["claude-work"], "routing": "roundRobin", "maxDepth": 2, "maxChildren": 3, "escalation": "parent", "parentWindowSeconds": 120, "role": "worker" } }
```

- `connections`: the connections children may run on, up to 16.
- `routing`: `"capacity"` (the default, also written `"leastUsed"`) or `"roundRobin"`.
- `maxDepth`: how deep children may nest, 1 to 8; 1 by default.
- `maxChildren`: how many children run at once, 1 to 16; 2 by default.
- `escalation`: `"parent"` to have children ask their parent first, or `"human"`.
- `parentWindowSeconds`: how long the parent has to answer, 10 to 3600; 120 by default.
- `role`: the role children take by default.

**`delegation.json`**, in the data folder, sets `escalation` and `parentWindowSeconds` for every repository; a repository's values win.

**`.avala/budget.json`**, `carvePerChild`: the share of the parent's remaining budget each child gets, 0.5 by default; see [Budgets and usage limits](budgets-and-usage-limits.md).

## Good to know

- Without a `delegation` section, delegation is refused.
- A delegation is refused when it is too deep, when the parent has too many children, when it asks for more autonomy than its parent or for a model its connection does not offer.
- A child's work that conflicts with its parent's is reported, not merged.

## See also

- [Budgets and usage limits](budgets-and-usage-limits.md)
- [Permissions and autonomy](permissions-and-autonomy.md)
- [Best of N](best-of-n.md)
