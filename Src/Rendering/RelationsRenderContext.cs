using Gremlinq.Models;

namespace Gremlinq.Rendering;

/// <summary>All data the <see cref="RelationsCanvasRenderer" /> needs to produce a single frame.</summary>
public sealed record RelationsRenderContext(
    VertexItem SourceVertex,
    IReadOnlyList<GraphSchemaEdge> AllSchemaEdges,
    EdgeLabelItem? SelectedEdge,
    VertexItem? SelectedDest,
    double Width,
    double Height);