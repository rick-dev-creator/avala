# Claude Code provider

**Accepted**

`Avala.ClaudeCode` is the first real provider plugin. It drives the `claude` command line in its headless streaming mode and translates its protocol into the [agent contract](core.md#agents); nothing else in Avala knows it exists. This page records how the protocol maps onto the contract and where each fact comes from. Facts marked *observed* were read from the CLI 2.1.295 on this machine, in throwaway repositories, and are pinned by the transcripts in `tests/transcripts/claude-code`.

## Sources

| Source | What it settles |
| --- | --- |
| [CLI reference](https://code.claude.com/docs/en/cli-reference) | `-p`, `--input-format`/`--output-format stream-json`, `--verbose`, `--include-partial-messages`, `--permission-prompt-tool`, `--permission-mode`, `--mcp-config`, `--strict-mcp-config`, `--setting-sources`, `--resume`, `--model`, `--effort` |
| [Headless mode](https://code.claude.com/docs/en/headless) | Streaming JSON in and out; one process for several turns; a `result` message ends each turn |
| [Agent SDK, TypeScript reference](https://code.claude.com/docs/en/agent-sdk/typescript) | The message types: `system` `init`, `assistant`, `user`, `stream_event`, `result` with `usage`, `total_cost_usd` and `modelUsage`, `rate_limit_event`; `PermissionResult`; `AskUserQuestion` input and answers |
| [Handling user input](https://code.claude.com/docs/en/agent-sdk/user-input) and [SDK permissions](https://code.claude.com/docs/en/agent-sdk/permissions) | `AskUserQuestion` and `ExitPlanMode` reach the host as permission requests; answers go back in `updatedInput.answers`; the order in which hooks, rules, modes and the prompt are consulted |
| [Hooks](https://code.claude.com/docs/en/hooks) | A `PreToolUse` hook's `permissionDecision` of `ask` sends a call to the permission prompt even when a rule or Claude's own read-only classification would allow it |
| [MCP](https://code.claude.com/docs/en/mcp) | `mcp__<server>__<tool>` names; server types, `sdk` among them, registered by the host only |
| [Environment variables](https://code.claude.com/docs/en/env-vars) and [authentication](https://code.claude.com/docs/en/authentication) | `CLAUDE_CONFIG_DIR`; `ANTHROPIC_API_KEY` wins over a subscription login in `-p`; `CLAUDE_CODE_ENABLE_TASKS`; `DISABLE_AUTOUPDATER` |
| [Sessions](https://code.claude.com/docs/en/agent-sdk/sessions) | Session ids, `--resume`, transcripts under `<config>/projects/<encoded cwd>/<id>.jsonl` |
| [Tools reference](https://code.claude.com/docs/en/tools-reference) | Built-in tool names and inputs |
| The Python Agent SDK's source, `anthropics/claude-agent-sdk-python`, `_internal/query.py` and `_internal/transport/subprocess_cli.py` | Not documentation: the wire format of the control protocol, `control_request`/`control_response`, `initialize` with hooks, `hook_callback`, `mcp_message` answered with `{ "mcp_response": … }`, and how `sdk` servers are passed in `--mcp-config` |

## The process

The adapter starts one `claude` process per session through `SessionOptions.Processes`, so it and everything it runs belong to the session's process tree, in the session's working directory:

```
claude -p --input-format stream-json --output-format stream-json --verbose --include-partial-messages
       --permission-mode default --permission-prompt-tool mcp__avala__permission_prompt
       --mcp-config {"mcpServers":{"avala":{"type":"sdk","name":"avala"}}} --strict-mcp-config
       --setting-sources "" [--model <model>] [--effort <effort>] [--resume <session id>]
```

- **One process, many turns** (headless docs, observed). Each user turn is a line `{"type":"user","message":{"role":"user","content":"…"}}` on stdin; the turn ends with a `result` line. The process stays alive between turns.
- **Settings are not read** (`--setting-sources ""`): no user, project or local `settings.json`, so no allow rule and no hook of the user or of the repository can let an action past Avala's policy. `--strict-mcp-config` likewise loads no MCP server but Avala's. The repository's `CLAUDE.md` still applies. The connection's `model` and `effort` settings are passed as flags.
- **The connection is the account.** The adapter removes every inherited credential and session variable (`CLAUDE_CONFIG_DIR`, `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN`, `CLAUDE_CODE_OAUTH_TOKEN`, and the variables a parent Claude Code session sets, such as `CLAUDECODE` and `CLAUDE_CODE_MESSAGING_TOKEN`) and sets only what the connection resolved to: its configuration folder as `CLAUDE_CONFIG_DIR`, its key as `ANTHROPIC_API_KEY`. The default folder `~/.claude` is left unset instead, because the CLI keeps that login's global configuration in `~/.claude.json` and, on macOS, its keychain entry under the default name. It also sets `CLAUDE_CODE_ENABLE_TASKS=0`, so the agent keeps its plan with `TodoWrite`, whose input is the whole plan, and `DISABLE_AUTOUPDATER=1`.
- **Interrupt** is a control request, `{"type":"control_request","request_id":"…","request":{"subtype":"interrupt"}}`; the CLI acknowledges it and ends the turn with a `result` of subtype `error_during_execution` (observed). **Stop** closes stdin and kills the process tree. A process that ends on its own fails the event stream, which Agents reports as `SessionEnded` with `Crashed`.
- **Transcripts.** A connection setting `transcripts` names a folder where the adapter writes every line it exchanges with the CLI, one JSON Lines file per process, for debugging and for the replay fixtures below.

## Protocol mapping

| Claude Code | Avala |
| --- | --- |
| `SendAsync` | `TurnStarted`, then the user line |
| `system` `init` with `session_id` | `ResumeTokenIssued`, once per turn, see [resume](#resume) |
| `stream_event` `content_block_start` of a `text` block, `text_delta`, `content_block_stop` | `ItemStarted` `Message`, `ItemProgressed` per delta, `ItemCompleted` |
| The same for a `thinking` block, `thinking_delta` | `Reasoning`. Some models stream an empty thinking text with only a signature |
| `assistant` text or thinking block whose message was not streamed | One item, started, progressed and completed at once |
| `assistant` `tool_use` | `ItemStarted` with the kind and a title. The final input of the `assistant` message is used, never the partial JSON, which the CLI may still rewrite (observed: a `cd <cwd> &&` prefix dropped) |
| `user` `tool_result` | `ItemProgressed` with the output, at most 16 KiB, then `ItemCompleted`: succeeded, failed when `is_error`, cancelled when Avala denied it, without its output |
| `TodoWrite` | `PlanUpdated` with every todo: `pending`, `in_progress`, `completed` |
| `rate_limit_event` | `LimitReported` per window of `unifiedWindows`: `five_hour` as `5h`, `seven_day` as `7d`, its `utilization` and `resetsAt`, a Unix time |
| `result` | `UsageReported`: `usage` is the turn's tokens, `output_tokens_details.thinking_tokens` the reasoning; `total_cost_usd` is the conversation's running total, so the cost is its difference from the previous total (observed). Then `ResumeTokenIssued` with the new total, and `TurnCompleted`: `Finished` for `success`, `Interrupted` after an interruption, `Failed` otherwise, items still open closed first |

| Tool | Kind | Target of its permission |
| --- | --- | --- |
| `Edit`, `Write`, `MultiEdit`, `NotebookEdit` | `FileEdit` | The file's full path |
| `Bash`, `PowerShell` | `Command` | The command line |
| `Grep`, `Glob`, `LS` | `Search` | The pattern |
| `WebFetch`, `WebSearch` | `Web` | The URL or the query |
| `mcp__<server>__<tool>`, other than Avala's | `Mcp` | The tool's name |
| `Agent`, `Task` | `Subagent` | Its description |
| Anything else, `Read` included | `Other` | `Read`'s path, or the tool's name |

## Avala's MCP server

The adapter is itself the MCP server it gives Claude Code, of type `sdk`: the CLI forwards every JSON-RPC message of that server over the session's own stdin and stdout as a `control_request` of subtype `mcp_message`, and the adapter answers with a `control_response` holding `{ "mcp_response": <JSON-RPC> }` (Python SDK source; observed). It handles `initialize`, `notifications/*`, `tools/list` and `tools/call`.

- **Why `sdk`.** It needs no socket, no port, no second executable and no network: the server lives in the session object, dies with it, and nothing else on the machine can reach it. A `stdio` server would be a separate program the CLI starts and that would have to reach back into Avala; an `http` server would listen on a port any local process could call.
- **Its tools** are `permission_prompt` and every harness tool in `SessionOptions.Tools`, with their descriptions and input schemas. Claude Code may defer loading them through its `ToolSearch` tool (observed).

### Permissions

- **The prompt tool.** `--permission-prompt-tool mcp__avala__permission_prompt` makes the CLI call `permission_prompt` with `{ tool_name, input, tool_use_id }` for every call its rules do not settle, and expects a text result holding `{"behavior":"allow","updatedInput":{…}}` or `{"behavior":"deny","message":"…"}` (observed).
- **Every action reaches the policy.** The CLI allows read-only commands such as `ls` or `sleep` on its own (observed: `sleep 20 && echo done` ran unasked). So the adapter registers a `PreToolUse` hook for every tool in the `initialize` control request; the CLI calls it as a `hook_callback`, and the adapter answers `permissionDecision: "ask"` for every tool that acts, which sends it to the prompt (observed: `ls` then asked). Reading tools, `Read`, `Grep`, `Glob`, the plan and question tools, `ToolSearch`, `Skill` and the subagent tool, are left to Claude Code, which asks only outside the working directory.
- **The prompt becomes `PermissionRequested`** with the item of its `tool_use_id`, its kind, title and target, in `AskEveryTime`; `AllowEdits` answers edits itself and `AllowAll` everything. The answer of `RespondAsync` goes back as `allow` with the input unchanged, or `deny` with the decision's message, and the adapter reports `PermissionResolved`. Prompts arrive one after the other or at once; the adapter asks one at a time, as the contract requires.
- Avala's own tools are allowed at once, and `permission_prompt` itself is always denied if the model calls it.

### Forms

- **`AskUserQuestion`** reaches the prompt like any tool (user-input docs, observed). It becomes a `Question` form: one field per question, `q1`, `q2` and so on, its `header` and `question`, single or multiple choice by `multiSelect`, its options with their descriptions, free text accepted as Claude Code's "Other", and the option whose label ends with `(Recommended)` marked recommended, the convention Claude follows. The answer returns `allow` with `updatedInput.answers`, keyed by question text, the chosen labels and any text joined by `, `; a declined form returns `deny` with its message. `FormAnswered` follows, and the `tool_result` closes the item.
- **`ExitPlanMode`** becomes a `PlanApproval` form whose context is the plan and whose one confirmation field accepts a comment; confirmed it allows leaving plan mode, otherwise it denies with the comment. Avala never starts a session in plan mode; the agent can enter it itself.

### Harness tools

- A `Canvas` tool's call opens a canvas with `CanvasStarted`, its `title` and `mediaType`, and its `content` as one `ItemProgressed` when the `assistant` message arrives; the `tools/call` that follows is answered at once and closes the item, failed when the input lacks one of the three fields. The content arrives whole, not in chunks: extracting it from partial JSON is left for later.
- An `Executed` tool's `tools/call` is `ToolCalled`, with the item of its `_meta.claudecode/toolUseId`, and stays pending until `ReturnAsync`, which answers the JSON-RPC call, reports `ToolReturned` and closes the item. Several calls may be pending at once.

## Resume

The token is `<session id>/<total cost so far>`. The session id resumes the conversation with `--resume`; the total lets the first turn of the resumed session report only the cost it added, since `total_cost_usd` carries on across `--resume` (observed). Before starting the process the adapter checks that the connection's configuration folder holds `projects/*/<session id>.jsonl`; a token it cannot read, or a conversation another connection holds, is `CannotResume`, so a token of one account never opens on another.

## Accounts and capacity

- **The account** of a session is read when it opens from the login's global configuration, never from its credentials: `oauthAccount.accountUuid` as the identifier and `emailAddress` as the label, in `<folder>/.claude.json`, or `~/.claude.json` for the default folder (undocumented, observed). An API key connection is `api-key:<fingerprint>`. The `initialize` response carries the same identity, `account.email`, `organization`, `subscriptionType` and `apiProvider` (observed), and `claude auth status --json` prints it for a folder without opening a session.
- **Discovery.** `LoginFolders` implements `IConnectionDiscovery`: `~/.claude`, every `~/.claude-*` and the folder of `CLAUDE_CONFIG_DIR` that holds a login, `.credentials.json` or `.claude.json` (the default folder counts `~/.claude.json`), is a connection named after its folder, `claude`, `claude-work`, with the `login` source and the folder's absolute path. It only checks that the files exist and never reads them.
- **Capacity.** Each turn's `rate_limit_event` reports both windows of the subscription, each with its utilization and its reset time, so `LimitReported` always carries the window and `ResetsAt` that capacity routing compares. `status` is `allowed`, `allowed_warning` or `rejected`. Without a turn, the login's `.claude.json` keeps the last readings in `cachedUsageUtilization` (`five_hour` and `seven_day`, each a utilization in percent and `resets_at`), an undocumented cache a future reader could use before a connection's first session.

## Recording and replay

- **Agnostic recordings.** The real jobs of `RealClaudeCodeTests` were recorded by the Recording plugin and committed as `tests/recordings/claude-code-*.json` with their expectations; the host tests replay them in the application, and the conformance kit checks them through the simulator, answering each permission as recorded.
- **Protocol transcripts.** The same sessions' transcripts, redacted, are in `tests/transcripts/claude-code`. `Avala.ClaudeCode.Replay`, a small program in the tests, plays one as a fake `claude`: it writes the recorded output, reads the adapter's input and checks that each line matches the recorded one by type, request and decision. The conformance kit runs the real adapter against it, the plugin given the replayer as its executable: canvas, processes, forms, denial, harness tool, resume and two logins.
- **Real runs.** `RealClaudeCodeTests` in the host tests run real Claude Code only with `AVALA_REAL_CLAUDE=1`, in temporary repositories, with `haiku`, on the login of `AVALA_REAL_CLAUDE_LOGIN` (default `~/.claude-work`) and, for the second account, `AVALA_REAL_CLAUDE_SECOND_LOGIN` (default `~/.claude`). They write recordings, expectations, redacted transcripts and the cost to `AVALA_REAL_CLAUDE_OUT`, to be read before anything is committed.

## Differences from the simulator

- Claude Code often writes a file with `Bash` (`printf … > FILE`) rather than `Write`; such a change is a command, so no edit is recorded and the replay does not recreate the file.
- Reasoning may arrive as empty thinking blocks.
- `ToolSearch` items appear before Claude calls Avala's tools for the first time.
- A turn's cost is a difference of running totals, and the totals are binary floating point, such as `0.0022790500000000003`.

## Not yet

- Streaming a canvas's content in chunks from the partial input JSON.
- Mapping the `Task*` tools to the plan when a user turns them back on.
- Subagent text, forwarded with `--forward-subagent-text`.
- `api_retry`, `compact_boundary` and the other `system` messages are ignored.
