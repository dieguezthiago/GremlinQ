using GremlinQ.Core.Models;

namespace GremlinQ.Core.Services;

public interface IGremlinQueryService
{
    /// <summary>Executes a Gremlin query and returns serialisable items with timing metadata.</summary>
    Task<QueryResult> ExecuteAsync(string query);
}
