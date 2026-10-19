# Delegation and orchestration

An agent can hand self-contained work to sub-agents, which run as child jobs governed and verified like any other.

## What it does

- **Avala offers the agent a `delegate` tool** only when the repository's committed `.avala/jobs.json` declares a `delegation` section; elsewhere the agent is not offered `delegate`, `answer_child` or `wait_child` at all, so it knows from its first turn that it cannot delegate. Each call starts a child job in a worktree made from the parent's current work, on a connection Avala chooses; several calls run in parallel.
- **A child is a job like any other**: its own session, permissions, checks and budget. When it ends, its verified work is merged into the parent's worktree, and the parent receives its summary, the files it changed and its evidence, with its state, its harness and, when it failed, why.
- **Every sub-agent reads the same way**, whether it is a child job or one of the harness's own sub-agents: **working**, **waiting** (a permission or a question of its own waits for you or its parent), **completed**, **failed**, **stopped** (stopped by the agent, or a child you discarded) or **lost** (it was still working in the background when its turn, its session or Avala ended, or a child whose session was lost). The orchestrator is told the same words.
- **The agent can ask Avala about its sub-agents.** Every agent that takes Avala's tools, in any repository, has a `subagents` tool that lists its job's sub-agents, the harness's own and its child jobs, each with its state and, for one that waits, what it waits for; it works after a restart too. Where delegation is allowed, a `stop_child` tool stops one of its own child jobs: the child is discarded and its report, stopped, answers the call.
- **A child that reaches its usage limit moves on.** When neither the repository nor your machine sets usage-limit rules, a child at its limit is handed to another of the connections the `delegation` section lists, keeping its worktree and work, or waits for its window to reset. Its parent is told it is held and why, and can keep waiting or stop it.
- **Roles.** A `worker`, the default, changes files. A `reviewer` or `research` child only reads and reports back without changing the parent's worktree.
- **Children ask their parent first**, when the repository says so: the parent answers through `answer_child`, and allows only what it would be allowed to do itself; anything else goes to you.
- **A child never runs looser than its parent**, and its budget is carved out of its parent's.

## How to use it

1. **Allow delegation** in the `delegation` section of `.avala/jobs.json` and commit it.
2. **Ask the agent to split the work.** It decides when to call `delegate`, and may name one of the connections the `delegation` section lists for a child; any other connection is refused with the list to choose from.
3. **Follow the tree beside the conversation.** In the header of a job that delegated, or of one of its children, the tree glyph opens the **Delegation** pane. The job you started is at the top with how much context it has left (marked "compacted" when its harness compacted it), the tokens of the whole tree and how many of its jobs need you, are running, are done or were refused. Each child below is one row: what it does now, the context it has left and what it spent, and how many sub-agents it runs. Children that need you come first, then running ones; finished ones fold into one "N done" line. Click a row to open its card, one at a time: its harness and connection, its last three actions and how long ago they started, its spend against its carve and its tokens, and its harness's own sub-agents as dashed pills with the command each runs now, how many tools it has used and how many tokens its conversation holds. **Peek** follows the job's conversation inside its card, its latest rows as they arrive, with a permission or a question it asks answerable there and its own composer to send it a message, without leaving the conversation you are in; **Open conversation** opens the job. Sub-agents' tools stay out of the conversation, which keeps one row per sub-agent; click a sub-agent's pill to list the tools it ran, newest first. A permission one of them asks for still shows in the conversation. When a job you are viewing starts a sub-agent, the pane opens by itself, also for a job that delegated nothing; once you close it, it opens again only for the next sub-agent. The footer counts the jobs on each connection and says where it compacts a conversation, such as "compacts at 250k", the same rule for every job on that connection unless a child's role sets its own window under `roles`, when it names each, such as "compacts at 120k or 250k". The pane takes the preview's place beside the conversation, keeps its width and hides in focus mode.
4. **See every orchestration at once** on **Overview**, **Delegation**: one tree per job that delegated, most recent activity first, each headed by its title, repository, jobs and agents, how many need you, tokens and spend. **Active**, the default, shows the trees with any job still at work or waiting for you; **Finished** and **All** show the rest, finished ones collapsed to their header. Pick a repository or a connection to keep only the trees that touch it. Click a header to collapse or expand its tree; its rows open and peek as in the pane. The job inspector's **Delegation** section lists a job's children too.
5. **Answer a child that asks** from the card "Sub-agent … is waiting for this job" when it comes to you; it names the child's harness, such as "Sub-agent … on Claude Code is waiting for this job", once Avala knows it.

## Settings and files

**`.avala/jobs.json`**, `delegation` section:

```json
{ "delegation": { "connections": ["claude-work"], "routing": "roundRobin", "maxDepth": 2, "maxChildren": 3, "escalation": "parent", "parentWindowSeconds": 120, "role": "worker" } }
```

- `connections`: the connections children may run on, up to 16. The agent may name one of them for a child; a child at its usage limit moves among them unless usage-limit rules say otherwise.
- `routing`: `"capacity"` (the default, also written `"leastUsed"`) or `"roundRobin"`.
- `maxDepth`: how deep children may nest, 1 to 8; 1 by default.
- `maxChildren`: how many children run at once, 1 to 16; 2 by default.
- `escalation`: `"parent"` to have children ask their parent first, or `"human"`.
- `parentWindowSeconds`: how long the parent has to answer, 10 to 3600; 120 by default.
- `role`: the role children take by default.
- `roles`: per role, the `model`, `effort` and `compactAt` a child of that role runs with when its parent's call leaves them out. `compactAt` is the token count where the child's context compacts, 20,000 to 1,000,000, in place of its connection's; a harness without a compaction window ignores it. A role name other than `worker`, `reviewer` or `research`, or a value out of range, makes the section invalid.

```json
{ "delegation": { "roles": { "research": { "model": "haiku", "effort": "low", "compactAt": 120000 }, "worker": { "model": "opus" } } } }
```

**`delegation.json`**, in the data folder, sets `escalation` and `parentWindowSeconds` for every repository; a repository's values win.

**`.avala/budget.json`**, `carvePerChild`: the share of the parent's remaining budget each child gets, 0.5 by default; see [Budgets and usage limits](budgets-and-usage-limits.md).

## Good to know

- Without a committed `delegation` section, the delegation tools are not offered, and a call that arrives anyway is refused. A section written in the worktree but not committed does not count.
- A harness's own sub-agents, such as Claude Code's, may run in the background after the agent's reply. The job keeps working while they do: it is not sent to review, and its agent is not ended while idle under them. If its turn ends anyway, for example because you stopped it, the end of the turn says which sub-agents' work is lost.
- A delegation is refused when it is too deep, when the parent has too many children, when it asks for more autonomy than its parent or for a model its connection does not offer.
- A child's work that conflicts with its parent's is reported, not merged.
- When the parent ends, approved, discarded or failed, every child that has not ended is discarded, including one left for review or held, so no sub-agent keeps running for a job that is gone.

## See also

- [Budgets and usage limits](budgets-and-usage-limits.md)
- [Permissions and autonomy](permissions-and-autonomy.md)
- [Best of N](best-of-n.md)
