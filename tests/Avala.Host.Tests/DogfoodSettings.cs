using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Host.Composition;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Runtime.Diagnostics;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Presentation;
using Avala.Sdk.Processes;
using Avala.Sdk.Regions;
using Avala.Shell;
using Avala.Shell.Regions;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal static class DogfoodSettings
{
    internal const string Gate = "AVALA_DOGFOOD";

    internal const string Connection = "dogfood";

    internal const string Instruction =
        "Build a small Markdown notes CLI in Node (no dependencies): `node notes.js add <title> <text>` writes a note as a Markdown file "
        + "in a notes/ folder, `node notes.js list` lists the notes, and `node notes.js search <words>` prints the notes whose title or text "
        + "contain the words. Keep the logic in a module separate from the command line, so unit tests runnable with `node --test` import the real code "
        + "and work in a temporary folder. Add a README with how to run it. Do not start servers or install anything.";

    internal const string ChangeRequest =
        "Add a `delete <id>` command that removes one note, and a `--tag <tag>` option on add and list that stores tags in the note's front matter "
        + "and filters by them, both covered by `node --test` unit tests. Keep everything else working.";

    internal static readonly string[] Groups = ["NeedsYou", "Running", "ReadyForReview", "Done"];

    internal static readonly JobStatus[] Settled = [JobStatus.AwaitingReview, JobStatus.NeedsHelp, JobStatus.Failed, JobStatus.Approved, JobStatus.Discarded];

    internal static readonly string[] Refused = ["sudo", "curl ", "wget ", "ssh ", "scp ", "git push", "git remote", "npx ", "pip ", "pip3 ", "-g ", "--global", "http.server", "live-server", "npm start", "npm run dev", "npm run serve", "serve -", "nohup", "&>/dev/null &", " & "];

    internal static string Model => Environment.GetEnvironmentVariable("AVALA_DOGFOOD_MODEL") is { Length: > 0 } model ? model : "sonnet";

    internal const string SlowCheck = "sleep 21";

    internal static readonly TimeSpan Guard = TimeSpan.FromMinutes(45);

    internal static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    internal static string Root => Environment.GetEnvironmentVariable("AVALA_DOGFOOD_ROOT") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Path.GetTempPath(), "avala-dogfood");

    internal static string Screens => Environment.GetEnvironmentVariable("AVALA_DOGFOOD_SCREENS") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Root, "screens");

    internal static string Login => Environment.GetEnvironmentVariable("AVALA_DOGFOOD_LOGIN") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude-work");

    internal static string Resumed => Environment.GetEnvironmentVariable("AVALA_DOGFOOD_RESUME") ?? string.Empty;

    internal static string Rehearsal => Environment.GetEnvironmentVariable("AVALA_DOGFOOD_REHEARSAL") ?? string.Empty;

    internal static decimal SendBackBelow => decimal.Parse(Environment.GetEnvironmentVariable("AVALA_DOGFOOD_SEND_BACK_BELOW") is { Length: > 0 } value ? value : "0.90", CultureInfo.InvariantCulture);

    internal static decimal StopAt => decimal.Parse(Environment.GetEnvironmentVariable("AVALA_DOGFOOD_STOP_AT") is { Length: > 0 } value ? value : "1.85", CultureInfo.InvariantCulture);

    internal const string Permissions = """
        {
          "autonomy": "supervised",
          "formAnswers": "recommended",
          "rules": [
            { "name": "run the tests", "kind": "command", "target": "node --test*", "answer": "allow" },
            { "name": "run npm tests", "kind": "command", "target": "npm test*", "answer": "allow" },
            { "name": "git status", "kind": "command", "target": "git status*", "answer": "allow" },
            { "name": "git diff", "kind": "command", "target": "git diff*", "answer": "allow" },
            { "name": "git log", "kind": "command", "target": "git log*", "answer": "allow" },
            { "name": "list files", "kind": "command", "target": "ls*", "answer": "allow" },
            { "name": "no network", "kind": "web", "answer": "deny" },
            { "name": "no mcp", "kind": "mcp", "answer": "deny" }
          ]
        }
        """;

    internal const string Checks = """
        {
          "checks": [
            { "name": "tests", "command": "node", "arguments": ["--test"], "timeoutSeconds": 120 },
            { "name": "tests exist", "command": "bash", "arguments": ["-c", "set -o pipefail; node --test --test-reporter=tap | grep -Eq '^# pass [1-9]'"], "timeoutSeconds": 120 },
            { "name": "readme exists", "command": "test", "arguments": ["-f", "README.md"], "timeoutSeconds": 10 },
            { "name": "slow suite", "command": "bash", "arguments": ["-c", "sleep 21; node --test"], "timeoutSeconds": 120 }
          ]
        }
        """;

    internal const string Rehearsed = """
        {
          "checks": [
            { "name": "slow suite", "command": "bash", "arguments": ["-c", "sleep 21; true"], "timeoutSeconds": 60 }
          ]
        }
        """;

    internal const string Budget = """
        {
          "costPerJob": { "USD": 2.00 },
          "holdAtLimit": 0.95
        }
        """;
}
