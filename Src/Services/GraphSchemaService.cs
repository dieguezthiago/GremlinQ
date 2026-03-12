using System.Text.Json;
using Gremlinq.Models;
using Gremlinq.Services.Interfaces;

namespace Gremlinq.Services;

/// <summary>
///     Queries the Gremlin server for schema-level information: vertex types, edge types,
///     destination vertices, and full schema graphs.
/// </summary>
public sealed class GraphSchemaService : IGraphSchemaService
{
    private readonly IGremlinConnectionService _connection;

    public GraphSchemaService(IGremlinConnectionService connection)
    {
        _connection = connection;
    }

    public async Task<IReadOnlyList<VertexItem>> LoadVerticesAsync()
    {
        var results = await _connection.SubmitAsync("g.V().label().dedup()");

        return results
            .Select(r => ParseLabel((JsonElement)(object)r))
            .OfType<VertexItem>()
            .OrderBy(v => v.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<EdgeLabelItem>> LoadEdgesAsync(VertexItem vertex)
    {
        var outTask = _connection.SubmitAsync(
            $"g.V().hasLabel({vertex.GremlinRef}).outE().label().dedup()");
        var inTask = _connection.SubmitAsync(
            $"g.V().hasLabel({vertex.GremlinRef}).inE().label().dedup()");

        await Task.WhenAll(outTask, inTask);

        static IEnumerable<string> Labels(IEnumerable<dynamic> rs)
        {
            return rs.Select(r => ParseLabel((JsonElement)(object)r)?.Label).OfType<string>();
        }

        return Labels(outTask.Result)
            .Select(l => new EdgeLabelItem(l, true))
            .Concat(Labels(inTask.Result).Select(l => new EdgeLabelItem(l, false)))
            .OrderBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.IsOutgoing ? 0 : 1)
            .ToList();
    }

    public async Task<IReadOnlyList<VertexItem>> LoadDestVerticesAsync(
        VertexItem srcVertex, EdgeLabelItem edgeLabel)
    {
        var query = edgeLabel.IsOutgoing
            ? $"g.V().hasLabel({srcVertex.GremlinRef}).outE().hasLabel({edgeLabel.GremlinRef}).inV().label().dedup()"
            : $"g.V().hasLabel({srcVertex.GremlinRef}).inE().hasLabel({edgeLabel.GremlinRef}).outV().label().dedup()";

        var results = await _connection.SubmitAsync(query);

        return results
            .Select(r => ParseLabel((JsonElement)(object)r))
            .OfType<VertexItem>()
            .OrderBy(v => v.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<GraphSchemaEdge>> LoadRelationsEdgesAsync(VertexItem vertex)
    {
        var query =
            $"g.E().where(outV().hasLabel({vertex.GremlinRef}).or().inV().hasLabel({vertex.GremlinRef}))" +
            ".project('f','l','t').by(outV().label()).by(label()).by(inV().label()).dedup()";

        var results = await _connection.SubmitAsync(query);

        return results
            .Select(r => ParseSchemaEdge((JsonElement)(object)r))
            .OfType<GraphSchemaEdge>()
            .ToList();
    }

    public async Task<GraphSchema> LoadFullSchemaAsync()
    {
        var results = await _connection.SubmitAsync(
            "g.E().project('f','l','t').by(outV().label()).by(label()).by(inV().label()).dedup()");

        var edges = new List<GraphSchemaEdge>();
        var nodeSet = new HashSet<string>(StringComparer.Ordinal);

        foreach (var r in results)
        {
            var edge = ParseSchemaEdge((JsonElement)(object)r);
            if (edge is null) continue;
            edges.Add(edge);
            nodeSet.Add(edge.From);
            nodeSet.Add(edge.To);
        }

        var nodes = nodeSet
            .OrderBy(s => s)
            .Select(label => new GraphNode(label))
            .ToList();

        return new GraphSchema(nodes, edges);
    }

    // ── Parsers ───────────────────────────────────────────────────────────────

    private static VertexItem? ParseLabel(JsonElement el)
    {
        return el.ValueKind == JsonValueKind.String ? new VertexItem(el.GetString()!) : null;
    }

    private static GraphSchemaEdge? ParseSchemaEdge(JsonElement el)
    {
        string? f = null, l = null, t = null;

        // GraphSON2 g:Map: {"@type":"g:Map","@value":["f","..","l","..","t",".."]}
        if (el.TryGetProperty("@type", out var type) && type.GetString() == "g:Map" &&
            el.TryGetProperty("@value", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            var items = arr.EnumerateArray().ToList();
            for (var i = 0; i + 1 < items.Count; i += 2)
            {
                var k = items[i].ValueKind == JsonValueKind.String ? items[i].GetString() : null;
                var v = items[i + 1].ValueKind == JsonValueKind.String ? items[i + 1].GetString() : null;
                if (k == "f") f = v;
                else if (k == "l") l = v;
                else if (k == "t") t = v;
            }
        }
        else if (el.ValueKind == JsonValueKind.Object)
        {
            if (el.TryGetProperty("f", out var fv)) f = fv.GetString();
            if (el.TryGetProperty("l", out var lv)) l = lv.GetString();
            if (el.TryGetProperty("t", out var tv)) t = tv.GetString();
        }

        return f is not null && l is not null && t is not null
            ? new GraphSchemaEdge(f, l, t)
            : null;
    }
}