using GremlinQ.Core.Models;

namespace GremlinQ.Core.Abstractions;

public interface IGremlinQueryService
{
    /// <summary>Executes a Gremlin query and returns serialisable items with timing metadata.</summary>
    Task<QueryResult> ExecuteAsync(string query);
}
