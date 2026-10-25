using Avala.Agents.Contracts.Sessions;
using Avala.Verification.Checks;
using Avala.Verification.Contracts;

namespace Avala.Verification.Discovering;

internal static class ScoutBrief
{
    public const int LongestCheckSeconds = 1_800;

    public const string Opening = "Find out how this repository proves that a change to it is correct";

    public static HarnessTool Tool { get; } = new(
        ChecksProposal.Tool,
        "Propose the commands that check this repository. Avala runs each one again and keeps the ones that pass as the repository's checks.",
        """
        {
          "type": "object",
          "properties": {
            "checks": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "name": { "type": "string", "description": "One or two plain words, such as build, lint, typecheck or tests." },
                  "command": { "type": "string", "description": "The program alone. It is not run by a shell." },
                  "arguments": { "type": "array", "items": { "type": "string" }, "description": "The program's arguments, one string each." },
                  "timeoutSeconds": { "type": "number", "description": "About three times what the command takes, between 60 and 1800." }
                },
                "required": ["name", "command"],
                "additionalProperties": false
              }
            }
          },
          "required": ["checks"],
          "additionalProperties": false
        }
        """,
        ToolSurface.Executed);

    public static string Text(bool runs) => $$"""
        {{Opening}}, so Avala can run the same commands after every change an agent makes.

        Work in this worktree only and change none of its files. Do not stop to ask anything: decide on your own.

        1. Explore the repository: its manifests and build files, its scripts, its CI workflows, and its README, CONTRIBUTING, AGENTS.md or CLAUDE.md.
        2. Find the commands that build it, type-check it, lint it and run its tests, whatever its languages and tools are.
        {{(runs ? Running : Reading)}}
        5. Prefer few commands that cover the most: a single command that builds and tests is better than two that repeat the work.
        6. The checks run later in a fresh clone where nothing is installed or built yet. When the commands need dependencies, make the command that installs them, such as a clean install from the lock file, the first check.

        Then call the `{{ChecksProposal.Tool}}` tool once with the commands, in the order they should run, the fastest first:

        - `command` is the program alone and `arguments` its arguments, one string each. The command is not run by a shell, so use no `&&`, pipes, redirections or variables; when a step needs a shell, name the repository's own script instead.
        - `name` is one or two plain words, such as build, lint, typecheck or tests.
        - `timeoutSeconds` is about three times what the command takes, at least 60 and at most {{LongestCheckSeconds}}.
        - When nothing in the repository can be checked this way, call it with an empty list.

        Do not write `{{CheckDeclaration.RelativePath}}` or any other file: Avala runs your commands again and keeps the ones that pass.
        """;

    private static string Running => $"""
        3. Run each command here, the way a developer would after a fresh clone.
        4. Keep only the commands that finish and pass on the code as it is now. Leave out a command that fails, that hangs, that needs secrets, a network service or a person, that changes tracked files (a formatter that rewrites code, for example), or that takes longer than {LongestCheckSeconds / 60} minutes.
        """;

    private static string Reading => $"""
        3. Do not run any command: this repository asks a person before each one. Read the files instead, and judge from them which commands work.
        4. Propose only commands the repository's own files show working, such as the ones its CI runs. Leave out a command that needs secrets, a network service or a person, that changes tracked files (a formatter that rewrites code, for example), or that would take longer than {LongestCheckSeconds / 60} minutes.
        """;
}
