namespace GremlinQ.Core.Models;

public sealed record HistoryEntry(string Query)
{
    /// <summary>Returns the query collapsed to a single line for ComboBox display.</summary>
    public override string ToString()
    {
        return Query.ReplaceLineEndings(" ").Trim();
    }
}