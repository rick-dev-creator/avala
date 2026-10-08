namespace Avala.Sdk.Processes;

public sealed record ProcessRequest(string FileName, IReadOnlyList<string> Arguments, Option<string> WorkingDirectory = default);
