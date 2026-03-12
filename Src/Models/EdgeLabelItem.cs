namespace Gremlinq.Models;

public sealed record EdgeLabelItem(string Label, bool IsOutgoing)
{
    public string GremlinRef => $"'{Label.Replace("'", "\\'")}'";
}