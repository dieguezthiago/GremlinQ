using System.Text.Json;
using GremlinQ.Core.Abstractions;
using GremlinQ.Core.Models;

namespace GremlinQ.Infrastructure.Services;

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
            .Select(label => new VertexItem(label))
            .ToList();

        return new GraphSchema(nodes, edges);
    }

    public async Task<IReadOnlyList<SchemaProperty>> LoadVertexPropertiesAsync(VertexItem vertex)
    {
        var results = await _connection.SubmitAsync(
            $"g.V().hasLabel({vertex.GremlinRef}).limit(100).properties().project('k','v').by(key()).by(value())");
        return ParseSchemaProperties(results);
    }

    public async Task<IReadOnlyList<SchemaProperty>> LoadEdgePropertiesAsync(string edgeLabel)
    {
        var safeLabel = edgeLabel.Replace("'", "\\'");
        var results = await _connection.SubmitAsync(
            $"g.E().hasLabel('{safeLabel}').limit(100).properties().project('k','v').by(key()).by(value())");
        return ParseSchemaProperties(results);
    }

    private static IReadOnlyList<SchemaProperty> ParseSchemaProperties(IEnumerable<dynamic> results)
    {
        // Collect one representative value per key to infer the type.
        var byKey = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var r in results)
        {
            var el = (JsonElement)(object)r;
            string? key = null;
            JsonElement? valueEl = null;

            // GraphSON2 g:Map → {"@type":"g:Map","@value":["k","name","v","John"]}
            if (el.TryGetProperty("@type", out var type) && type.GetString() == "g:Map" &&
                el.TryGetProperty("@value", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var items = arr.EnumerateArray().ToList();
                for (var i = 0; i + 1 < items.Count; i += 2)
                {
                    var k = items[i].ValueKind == JsonValueKind.String ? items[i].GetString() : null;
                    if (k == "k" && items[i + 1].ValueKind == JsonValueKind.String)
                        key = items[i + 1].GetString();
                    else if (k == "v")
                        valueEl = items[i + 1];
                }
            }
            else if (el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("k", out var kv) && kv.ValueKind == JsonValueKind.String)
                    key = kv.GetString();
                if (el.TryGetProperty("v", out var vv))
                    valueEl = vv;
            }

            if (key is not null && valueEl.HasValue && !byKey.ContainsKey(key))
                byKey[key] = InferType(valueEl.Value);
        }

        return byKey
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new SchemaProperty(kv.Key, kv.Value))
            .ToList();
    }

    private static string InferType(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String                 => "String",
        JsonValueKind.True or
        JsonValueKind.False                  => "Boolean",
        JsonValueKind.Number                 => value.TryGetInt64(out _) ? "Integer" : "Float",
        JsonValueKind.Object                 => InferGraphSonType(value),
        _                                    => "Unknown"
    };

    private static string InferGraphSonType(JsonElement obj)
    {
        if (!obj.TryGetProperty("@type", out var t)) return "Unknown";
        return t.GetString() switch
        {
            "g:Int32" or "g:Int64"    => "Integer",
            "g:Float" or "g:Double"   => "Float",
            "g:Date" or "g:Timestamp" => "Date",
            "g:UUID"                  => "UUID",
            { } other                 => other.StartsWith("g:") ? other[2..] : other,
            null                      => "Unknown"
        };
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