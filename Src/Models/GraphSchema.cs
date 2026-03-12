namespace Gremlinq.Models;

/// <summary>Snapshot of the full graph schema: all vertex types and relationship triples.</summary>
public sealed record GraphSchema(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphSchemaEdge> Edges);