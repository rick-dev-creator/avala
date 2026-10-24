using Avala.Verification.Checks;
using Avala.Verification.Contracts;

namespace Avala.Verification.Discovering;

internal static class ScoutBrief
{
    public const int LongestCheckSeconds = 1_800;

    public static string Text { get; } = $$"""
        Find out how this repository proves that a change to it is correct, so Avala can run the same commands after every change an agent makes.

        Work in this worktree only and change none of its tracked files. Do not stop to ask anything: decide on your own.

        1. Explore the repository: its manifests and build files, its scripts, its CI workflows, and its README, CONTRIBUTING, AGENTS.md or CLAUDE.md.
        2. Find the commands that build it, type-check it, lint it and run its tests, whatever its languages and tools are.
        3. Run each command here, the way a developer would after a fresh clone. Install the dependencies first if the commands need them.
        4. Keep only the commands that finish and pass on the code as it is now. Leave out a command that fails, that hangs, that needs secrets, a network service or a person, that changes tracked files (a formatter that rewrites code, for example), or that takes longer than {{LongestCheckSeconds / 60}} minutes.
        5. Prefer few commands that cover the most: a single command that builds and tests is better than two that repeat the work.

        Then write the commands you kept to `{{ChecksProposal.Path}}` at the root of this worktree, as JSON in this shape:

        ```json
        {
          "checks": [
            { "name": "build", "command": "<program>", "arguments": ["<argument>", "<argument>"], "timeoutSeconds": 300 }
          ]
        }
        ```

        - `command` is the program alone and `arguments` its arguments, one string each. The command is not run by a shell, so use no `&&`, pipes, redirections or variables; when a step needs a shell, name the repository's own script instead.
        - `name` is one or two plain words, such as build, lint, typecheck or tests.
        - `timeoutSeconds` is about three times what the command took when you ran it, at least 60 and at most {{LongestCheckSeconds}}.
        - List the commands in the order they should run, the fastest first.
        - When nothing in the repository can be checked this way, write `{ "checks": [] }`.

        Do not write `{{CheckDeclaration.RelativePath}}` yourself: Avala runs your commands again and keeps the ones that pass.
        """;
}
