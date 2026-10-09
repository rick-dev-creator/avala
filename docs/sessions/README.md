# Sessions

A record of every working session on Avala: what was built, how many tokens it took and what it would have cost at Anthropic's API list prices. Together with the lines of code, it measures how economically this kind of software can be built when the practices are right.

The cost is what the same tokens would cost on the API. A subscription may have covered the actual work.

| Session | Model | Cost at API prices | Production lines at the end |
| --- | --- | ---: | ---: |
| [2026-10-08 · b9482f08](2026-10-08-b9482f08.md) | Claude Opus 5.5 | $140.01 | 9,062 |
| **Total** | | **$140.01** | |

Earlier design conversations happened in sessions shared with unrelated work and are not counted.

## Recording a session

At the end of every session, run the script on the session's Claude Code transcript and add a page here:

```
dotnet run scripts/session-cost.cs -- ~/.claude/projects/<project>/<session-id>.jsonl
dotnet run scripts/metrics.cs
```

The script reads the transcript and its subagents, counts every model response once, and prices it with [pricing.json](pricing.json). Update the prices there when Anthropic changes them, noting the date.
