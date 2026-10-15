using Avala.Sdk;

namespace Avala.Components.Attaching;

public sealed record Pasted(IReadOnlyList<string> Files, Option<byte[]> Image, string Text)
{
    public const int LongText = 4000;
}
