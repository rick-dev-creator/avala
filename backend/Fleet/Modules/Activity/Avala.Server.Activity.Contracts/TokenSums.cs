namespace Avala.Server.Activity.Contracts;

internal sealed record TokenSums(decimal Input, decimal Output, decimal CacheRead, decimal CacheWrite, decimal Reasoning);
