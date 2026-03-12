namespace Gremlinq.Models;

/// <summary>Result returned by a Gremlin query execution.</summary>
public sealed record QueryResult(
    IReadOnlyList<object> Items,
    long ElapsedMs,
    string ProfileName);