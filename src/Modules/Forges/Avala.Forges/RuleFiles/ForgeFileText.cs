using System.Text;
using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Sdk;

namespace Avala.Forges.RuleFiles;

internal static class ForgeFileText
{
    private static readonly JsonWriterOptions Indented = new() { Indented = true };

    public static string Of(ForgeSettings settings)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer, Indented))
        {
            writer.WriteStartObject();
            writer.WriteNumber("pollSeconds", (int)settings.Poll.TotalSeconds);
            writer.WriteStartArray("forges");

            foreach (var forge in settings.Forges)
            {
                Write(writer, forge);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    private static void Write(Utf8JsonWriter writer, ForgeDeclaration forge)
    {
        writer.WriteStartObject();
        writer.WriteString("name", forge.Name.Value);
        writer.WriteString("forge", forge.Forge);

        Text(writer, "url", forge.Url.Map(url => url.OriginalString));

        if (forge.Source != CredentialSource.None)
        {
            writer.WriteStartObject("credential");
            writer.WriteString("source", forge.Source == CredentialSource.Cli ? ForgeFileParser.CliSource : ForgeFileParser.EnvironmentSource);
            Text(writer, "reference", forge.Reference);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void Text(Utf8JsonWriter writer, string name, Option<string> value) =>
        _ = value.Match(
            found =>
            {
                writer.WriteString(name, found);

                return true;
            },
            () => false);
}
