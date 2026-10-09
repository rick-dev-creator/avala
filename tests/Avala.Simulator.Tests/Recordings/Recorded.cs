namespace Avala.Simulator.Tests.Recordings;

internal static class Recorded
{
    public const string TurnStarted = """{ "at": 10, "event": { "type": "turnStarted", "turn": 1 } }""";

    public const string Finished = """{ "at": 90, "event": { "type": "turnCompleted", "turn": 1, "outcome": "finished" } }""";

    public static readonly string[] Edit =
    [
        """{ "at": 0, "send": { "text": "[simulate: edit] Greet the team" } }""",
        TurnStarted,
        """{ "at": 11, "event": { "type": "resumeTokenIssued", "turn": 1, "token": "provider-token" } }""",
        """{ "at": 20, "event": { "type": "itemStarted", "turn": 1, "item": "edit", "kind": "fileEdit", "title": "Edit GREETING.md" } }""",
        """{ "at": 21, "event": { "type": "permissionRequested", "turn": 1, "item": "edit", "title": "Edit GREETING.md", "kind": "fileEdit", "target": "${workingDirectory}/GREETING.md" } }""",
        """{ "at": 30, "respond": { "item": "edit", "answer": "allow" } }""",
        """{ "at": 31, "event": { "type": "permissionResolved", "turn": 1, "item": "edit", "answer": "allow" } }""",
        """{ "at": 40, "file": { "item": "edit", "path": "docs/GREETING.md", "content": "# Hello\n" } }""",
        """{ "at": 41, "event": { "type": "itemProgressed", "turn": 1, "item": "edit", "text": "# Hello\n" } }""",
        """{ "at": 42, "event": { "type": "itemCompleted", "turn": 1, "item": "edit", "outcome": "succeeded" } }""",
        """{ "at": 50, "event": { "type": "usageReported", "turn": 1, "tokens": { "input": 10, "output": 2, "cacheRead": 0, "cacheWrite": 0, "reasoning": 1 }, "cost": { "amount": 0.25, "currency": "USD" } } }""",
        Finished,
    ];

    public static readonly string[] Question =
    [
        TurnStarted,
        """
        { "at": 20, "event": { "type": "formRequested", "turn": 1, "item": "question", "form": {
            "purpose": "question", "title": "Choose a database", "context": "Orders",
            "fields": [ { "id": "database", "header": "Database", "prompt": "Which one?", "kind": "singleChoice",
              "options": [ { "label": "PostgreSQL", "description": "Relational", "recommended": true }, { "label": "SQLite", "description": "A file", "recommended": false } ],
              "acceptsFreeText": true } ] } } }
        """,
        """{ "at": 30, "answer": { "item": "question", "fields": [ { "field": "database", "chosen": ["PostgreSQL"], "confirmed": false } ], "declined": false } }""",
        """{ "at": 31, "event": { "type": "formAnswered", "turn": 1, "item": "question", "answer": { "item": "question", "fields": [ { "field": "database", "chosen": ["PostgreSQL"], "confirmed": false } ], "declined": false } } }""",
        """{ "at": 32, "event": { "type": "itemCompleted", "turn": 1, "item": "question", "outcome": "succeeded" } }""",
        Finished,
    ];

    public static string Session(params string[] entries) => SessionIn("askEveryTime", entries);

    public static string SessionIn(string permissions, params string[] entries) => Recording(permissions, string.Empty, entries);

    public static string SessionOf(string account, params string[] entries) => Recording("askEveryTime", $"\"account\": {account},", entries);

    private static string Recording(string permissions, string account, string[] entries) =>
        $$"""
        {
          "format": "avala-recording",
          "version": 1,
          "recordedAt": "2026-10-09T08:30:00+00:00",
          "provider": { "id": "claude-code", "name": "Claude Code" },
          "capabilities": { "acceptsTools": { "surfaces": ["canvas", "executed"] }, "asksForms": {}, "interruptible": {}, "reportsCost": { "currency": "USD" }, "reportsLimits": { "windows": ["5h", "7d"] }, "reportsUsage": {}, "resumable": {} },
          {{account}}
          "options": { "permissions": "{{permissions}}", "resumed": false, "tools": [] },
          "entries": [ {{string.Join(",\n", entries)}} ]
        }
        """;
}
