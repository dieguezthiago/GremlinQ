namespace GremlinQ.Core.Models;

/// <summary>Snapshot of the full graph schema: all vertex types and relationship triples.</summary>
public sealed record GraphSchema(
    IReadOnlyList<VertexItem> Nodes,
    IReadOnlyList<GraphSchemaEdge> Edges);
