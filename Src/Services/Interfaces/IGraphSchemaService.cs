using GremlinQ.Models;

namespace GremlinQ.Services.Interfaces;

public interface IGraphSchemaService
{
    Task<IReadOnlyList<VertexItem>> LoadVerticesAsync();
    Task<IReadOnlyList<EdgeLabelItem>> LoadEdgesAsync(VertexItem vertex);
    Task<IReadOnlyList<VertexItem>> LoadDestVerticesAsync(VertexItem srcVertex, EdgeLabelItem edgeLabel);

    /// <summary>Loads all schema edges where <paramref name="vertex" /> is either endpoint.</summary>
    Task<IReadOnlyList<GraphSchemaEdge>> LoadRelationsEdgesAsync(VertexItem vertex);

    /// <summary>Loads the complete schema (all vertex types + all edge triples) for the Graph tab.</summary>
    Task<GraphSchema> LoadFullSchemaAsync();
}