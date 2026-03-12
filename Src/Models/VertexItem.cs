namespace Gremlinq.Models;

public sealed record VertexItem(string Label)
{
    /// <summary>Gremlin-safe label reference for use in hasLabel().</summary>
    public string GremlinRef => $"'{Label.Replace("'", "\\'")}'";
}