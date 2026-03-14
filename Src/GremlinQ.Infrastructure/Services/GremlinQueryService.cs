using System.Diagnostics;
using GremlinQ.Core.Abstractions;
using GremlinQ.Core.Models;

namespace GremlinQ.Infrastructure.Services;

/// <summary>Executes Gremlin queries and returns timed, serialisable results.</summary>
public sealed class GremlinQueryService(IGremlinConnectionService connection) : IGremlinQueryService
{
    public async Task<QueryResult> ExecuteAsync(string query)
    {
        var sw = Stopwatch.StartNew();
        var results = await connection.SubmitAsync(query);
        sw.Stop();

        var items = results.Cast<object>().ToList();
        return new QueryResult(items, sw.ElapsedMilliseconds, connection.ActiveProfile?.Name ?? "unknown");
    }
}