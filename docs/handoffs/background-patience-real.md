# Handoff: background patience with real Claude Code

Work in progress on branch `fix/background-patience-real`. It stopped early because the owner asked to wrap up. This page says what is known, what was changed and what is left.

## The question

The real test `ARealJobWhoseBackgroundWorkFallsSilentForTwoHoursIsHeldAsStalledAndItsWorkStoppedAsync` (`tests/Avala.Host.Tests/RealIdleRuns.cs`) asks Claude Code, on Haiku, to start `sleep 9137` with `run_in_background` and end its turn.

**Expected**, from the owner's decision in the method plan (`.method/docs/plan/core.md`, "Owner decisions (2026-10-22)", about line 1291): work left only in the background holds its job once it has been silent for two hours (`JobWatch.BackgroundPatience`, `src/Modules/Supervision/Avala.Supervision/Watching/JobWatch.cs:15`). Holding interrupts the turn, and that is supposed to stop the background command. So the job should end `NeedsHelp`, held `Stalled`, with no `sleep 9137` left running.

**Actual**, in an earlier run: after 31 real minutes the job was `AwaitingReview` with no hold.

## Findings

### 1. Claude Code stops a background command at its own timeout, 30 minutes by default

This is the most likely cause of the 31-minute result.

In Claude Code 2.1.304, the CLI binary reads `var s=1800000; function pLs(n){return Math.max(s,pae(n))}` and `function cme(){return Math.min(Math.max(7200000,zIe()),2147483647)}`. Here `zIe` is `BASH_MAX_TIMEOUT_MS` and `pae` is `BASH_DEFAULT_TIMEOUT_MS`. The Bash tool's description says that with `run_in_background`, "`timeout` is how long the command may run in the background (default 1800000, max 7200000); at that limit it is stopped and you are re-invoked".

So, unless the agent asks for a longer `timeout`, Claude Code kills the command after 30 minutes, notifies, and wakes the model. The model replies, the turn ends with a result, and the job goes to review. In Avala, that is the woken-turn path, which is correct: Avala follows the harness. The command is gone because the harness stopped it, so `AwaitingReview` here does not leave a live process behind.

This part was not observed end to end. The rerun was stopped at 17 minutes, before the 30-minute mark. The evidence is the CLI source and the tool description. A probe with a project `.claude/settings.json` holding `{"env":{"BASH_MAX_TIMEOUT_MS":"10800000"}}` made the model quote "default 1800000 ms, max 10800000 ms". That confirms the limit and shows the project settings can raise it.

**Consequence:** with Claude Code's defaults, background work cannot stay silent for two hours. Claude Code's own limit (30 minutes by default, two hours at most) always comes first. Avala's patience is only reached when the person raised `BASH_MAX_TIMEOUT_MS`, or when a notification is lost.

### 2. Avala's interrupt does not stop a background shell: a bug, fixed here

These probes ran directly against `claude -p --input-format stream-json`, Haiku, version 2.1.304. The transcript is in `/home/rick/.cache/avala-real-tmp/patience/probe-stop-task.jsonl`.

- After the turn's `result`, with `sleep 9139` running in the background, Avala sends a `control_request` with `{"subtype":"interrupt"}`. The CLI acknowledges it with `{"still_queued":[]}`, but the process keeps running (checked with `pgrep`).
- A `control_request` with `{"subtype":"stop_task","task_id":"<id>"}` kills the process. The CLI then emits `task_updated` (status `killed`), then `task_notification` (status `stopped`), then `background_tasks_changed`, then a success response. The model is not woken.
- Closing the CLI's standard input, as when the session ends, also kills its background tasks.

Avala holds a job through `HoldJob.HaltAsync` (`src/Modules/Jobs/Avala.Jobs/Holding/HoldJob.cs`), which calls `IAgents.InterruptAsync`. For a turn carried only by background work, the Claude Code adapter sends the interrupt and closes the turn `Interrupted` once Claude Code acknowledges it. It also records every background item as `Cancelled` (`StreamTranslator.Close`). Avala's record then says the command was cancelled while it still runs, which breaks the owner's decision that holding stops the background work.

**The fix:** `Conversation.Interrupt` (`src/Modules/ClaudeCode/Avala.ClaudeCode/Conversations/Conversation.cs`) now sends a `stop_task` (`Messages.StopTask`) with every interrupt, one for each open background shell (`ToolBook.BackgroundShells`: backgrounded, not closed, kind `Command`, task id known).

Background sub-agents are left alone, since Claude Code already stops them on an interrupt (fixture `interrupt-background-agents`). A `stop_task` that arrives after the shell has ended gets an error response, which the adapter ignores. A `task_notification` arriving after the acknowledgement is recognised as finished and wakes nothing.

**Test:** `InterruptingATurnKeptLiveByABackgroundCommandAlsoStopsThatCommandSinceClaudeCodesInterruptLeavesItRunning` (`tests/Avala.ClaudeCode.Tests/Conversations/InterruptTests.cs`). A compiling sabotage that empties `BackgroundShells` makes it fail. With the fix, the Host tests (Claude Code replays included) and the Claude Code tests pass.

### 3. The fake clock follows real time

The rerun logged every `JobProgressed` and `AgentActivity` with real time and fake time (`/home/rick/.cache/avala-real-tmp/patience/events.log`). Fake time stayed within one second behind real time throughout (`fake=14:14:21.97` at real `14:14:22.87`). The paced timer (`AdvanceTo(started + elapsed)`) and the `SystemTimerClock`'s one-tick auto-advance do not run ahead. **Ruled out:** a clock artifact as the cause of the 31-minute review.

### 4. The adapter carries the turn correctly

The rerun's events were:

1. `ItemStarted` "Run sleep 9137".
2. `PermissionRequested`, then `PermissionResolved` Allow.
3. `ItemBackgrounded`.
4. The message.
5. `UsageReported` and `ContextReported`, with no `TurnCompleted`.

The result was carried because the shell was backgrounded, and the job stayed `Running`. The real `tool_use_result` carries `"backgroundTaskId":"b5xtjhldq"`, and `task_started` carries `"task_type":"local_bash","is_backgrounded":true`. **Ruled out:** an event mapping that sends the job to review at once.

## Hypotheses, ranked

1. **Most likely:** the 31-minute review was Claude Code stopping the command at its 30-minute default and waking the model, which ended the turn. This is harness behaviour, and Avala follows it. Still to confirm: run the old test past 30 minutes and look for a `task_notification` with status `stopped` or similar, then a new assistant message and `result`.
2. **Confirmed and fixed:** when Avala's patience does fire, its hold leaves the shell running. That was a production bug.
3. **Ruled out:** the fake clock running ahead (finding 3), and an immediate wrong mapping (finding 4).

## What changed on this branch

- **Production:** `stop_task` for background shells on every interrupt (finding 2).
- **Unit test:** the one above.
- **Real test:**
  - It now commits `.claude/settings.json` with `{"env":{"BASH_MAX_TIMEOUT_MS":"10800000"}}`.
  - It asks for `timeout` 10800000, so Claude Code lets the command run past two hours and only Avala's patience can stop it.
  - It records the raw CLI traffic under `$AVALA_REAL_CLAUDE_OUT/raw`.
- **Docs:** the guide says how background work ends. The DocFX site excludes `handoffs/`.

## Rerun the real test

It takes about two hours and a few minutes.

```
cd /home/rick/Projects/avala-patience
export TMPDIR=/home/rick/.cache/avala-real-tmp
export AVALA_REAL_IDLE=1 AVALA_REAL_CLAUDE=1 AVALA_REAL_CLAUDE_LOGIN=$HOME/.claude-cuenta2
export AVALA_REAL_CLAUDE_OUT=/home/rick/.cache/avala-real-tmp/patience/out
dotnet build Avala.slnx
dotnet test --project tests/Avala.Host.Tests --no-build --filter-method "*ARealJobWhoseBackgroundWorkFallsSilentForTwoHours*"
```

Before it, make sure no `sleep 9137` is running (`pgrep -af "sleep 9137"`), since the test's last assertion uses `pgrep`.

## Next steps

1. **Run the real test above to a pass.** Expected: at about two hours of silence, the job is `NeedsHelp`, held `Stalled`, and `sleep 9137` is gone. If the model ignores the `timeout` request, Claude Code stops the command at two hours, races Avala's patience, and the job goes to review instead. Then make the instruction stricter.
2. **Turn the recorded tap into a replay fixture**, for example `background-shell-stalled`:

   ```
   dotnet run scripts/anonymize-transcript.cs -- <tap> background-shell-stalled "<what it shows>"
   ```

   Then add a Host replay test: the job is held `Stalled` after two hours of fake time, `stop_task` is sent, and the command ends `Cancelled`.
3. **Optionally, record the 30-minute shape as its own fixture** (without the settings file, about 35 minutes): a background command Claude Code stops at its default timeout, after which the agent wakes and the job awaits review.
4. **Update the method plan's owner-decision entry:** holding stops background shells through `stop_task`, since Claude Code's interrupt leaves them running. Also note Claude Code's own 30-minute default.
5. Run the full suite once more, then send an adversary round on the change, as the method asks.
