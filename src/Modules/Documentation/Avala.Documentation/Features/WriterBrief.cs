using System.Globalization;
using System.Text;
using Avala.Documentation.Contracts;
using Avala.Sdk;

namespace Avala.Documentation.Features;

internal sealed record WriterBrief(Feature Feature, int Number, VersionKind Kind, string Instruction, IReadOnlyList<string> Plan, Option<string> Previous)
{
    public string Text()
    {
        var brief = new StringBuilder();
        brief.Append(CultureInfo.InvariantCulture, $"Write version {Number} of the feature \"{Feature.Title}\" as one Markdown file, {DocumentationDraft.Path}. Write that file and change nothing else.\n\n");
        brief.Append(Kind == VersionKind.Proposed
            ? "This version records the plan the job's agent proposed, before it is built.\n\n"
            : "The job's plan changed. This version records the feature as the new plan describes it, and says what changed against the previous version and why.\n\n");
        brief.Append(CultureInfo.InvariantCulture, $"The job's instruction:\n\n{Instruction.Trim()}\n\nThe plan:\n\n");
        brief.Append(string.Concat(Plan.Select((step, index) => string.Create(CultureInfo.InvariantCulture, $"{index + 1}. {step}\n"))));
        Previous.Match(
            text => brief.Append(CultureInfo.InvariantCulture, $"\nThe previous version, to build on:\n\n{text.Trim()}\n"),
            () => brief);
        brief.Append(CultureInfo.InvariantCulture, $$"""

            How to write it:

            - Start with "# {{Feature.Title}}" and one paragraph on what the feature does at this version.
            - Draw it as one Mermaid C4 diagram in a mermaid code block: C4Context, C4Container or C4Component, at the level the plan works at. Add a C4Dynamic diagram for a flow that matters.
            - Give every element a lowercase alias, and follow the diagram with one section per element whose heading ends with {#alias}, saying what it does and what changed.
            - Read the code the plan touches to name the real containers and components. Do not change any code.
            """);

        return brief.ToString();
    }
}
