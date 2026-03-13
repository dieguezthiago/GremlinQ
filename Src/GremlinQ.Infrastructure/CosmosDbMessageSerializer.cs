using System.Text.Json;
using Gremlin.Net.Driver;
using Gremlin.Net.Driver.Messages;
using Gremlin.Net.Structure.IO.GraphSON;

namespace GremlinQ.Infrastructure;

/// <summary>
///     A message serializer that uses the standard <see cref="GraphSON2MessageSerializer" /> for
///     outbound requests but manually parses inbound responses, tolerating bare JSON numbers
///     (non-wrapped numerics) that Azure Cosmos DB emits for vertex IDs.
///     Results are returned as <see cref="JsonElement" /> values so that
///     <c>System.Text.Json</c> can re-serialize them without issues.
/// </summary>
internal sealed class CosmosDbMessageSerializer : IMessageSerializer
{
    private static readonly GraphSON2MessageSerializer _inner = new();

    /// <inheritdoc />
    public Task<byte[]> SerializeMessageAsync(RequestMessage requestMessage,
        CancellationToken cancellationToken = default)
    {
        return _inner.SerializeMessageAsync(requestMessage, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ResponseMessage<List<object>>?> DeserializeMessageAsync(byte[] message,
        CancellationToken cancellationToken = default)
    {
        var doc = JsonDocument.Parse(message);
        var root = doc.RootElement;

        var requestId = root.GetProperty("requestId").GetGuid();
        var statusEl = root.GetProperty("status");
        var statusCode = (ResponseStatusCode)statusEl.GetProperty("code").GetInt32();
        var statusMsg = statusEl.TryGetProperty("message", out var m) ? m.GetString() : null;

        Dictionary<string, object>? attrs = null;
        if (statusEl.TryGetProperty("attributes", out var attrsEl) &&
            attrsEl.ValueKind == JsonValueKind.Object)
        {
            attrs = [];
            foreach (var attr in attrsEl.EnumerateObject())
                attrs[attr.Name] = attr.Value.Clone();
        }

        var resultEl = root.GetProperty("result");
        var dataEl = resultEl.GetProperty("data");
        var resultData = ExtractList(dataEl);

        Dictionary<string, object>? meta = null;
        if (resultEl.TryGetProperty("meta", out var metaEl) &&
            metaEl.ValueKind == JsonValueKind.Object)
        {
            meta = [];
            foreach (var kv in metaEl.EnumerateObject())
                meta[kv.Name] = kv.Value.Clone();
        }

        // Use positional constructors discovered from compile errors:
        // ResponseStatus(code, attributes, message)
        // ResponseResult<T>(data, meta)
        // ResponseMessage<T>(requestId, status, result)
        var status = new ResponseStatus(statusCode, attrs, statusMsg);
        var result = new ResponseResult<List<object>>(resultData, meta);
        var response = new ResponseMessage<List<object>>(requestId, status, result);

        return Task.FromResult<ResponseMessage<List<object>>?>(response);
    }

    /// <summary>Unwraps a GraphSON list wrapper or plain array into a <c>List&lt;object&gt;</c>.</summary>
    private static List<object> ExtractList(JsonElement dataEl)
    {
        // Cosmos DB wraps the data array as {"@type":"g:List","@value":[...]}
        var array = dataEl;
        if (dataEl.ValueKind == JsonValueKind.Object &&
            dataEl.TryGetProperty("@value", out var inner))
            array = inner;

        var list = new List<object>();
        if (array.ValueKind == JsonValueKind.Array)
            foreach (var el in array.EnumerateArray())
                list.Add(el.Clone()); // JsonElement is serializable by System.Text.Json

        return list;
    }
}
