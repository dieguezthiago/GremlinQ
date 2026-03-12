using System.Diagnostics;
using GremlinQ.Models;
using GremlinQ.Services.Interfaces;

namespace GremlinQ.Services;

/// <summary>Executes Gremlin queries and returns timed, serialisable results.</summary>
public sealed class GremlinQueryService : IGremlinQueryService
{
    private readonly IGremlinConnectionService _connection;

    public GremlinQueryService(IGremlinConnectionService connection)
    {
        _connection = connection;
    }

    public async Task<QueryResult> ExecuteAsync(string query)
    {
        var sw = Stopwatch.StartNew();
        var results = await _connection.SubmitAsync(query);
        sw.Stop();

        var items = results.Cast<object>().ToList();
        return new QueryResult(items, sw.ElapsedMilliseconds, _connection.ActiveProfile?.Name ?? "unknown");
    }
}