namespace Avala.Sdk.Processes;

public sealed record ProcessRequest(string FileName, IReadOnlyList<string> Arguments, string? WorkingDirectory = null);
