using System.Windows.Controls;
using System.Windows.Media;
using GremlinQ.App.Models;
using GremlinQ.Core.Models;

namespace GremlinQ.App.Rendering;

/// <summary>Renders the force-directed schema graph onto the Graph tab canvas.</summary>
public sealed class GraphCanvasRenderer
{
    private readonly CanvasDrawingHelper _drawing;

    public GraphCanvasRenderer(CanvasDrawingHelper drawing)
    {
        _drawing = drawing;
    }

    public void Render(Canvas canvas, IList<GraphNode> nodes, IReadOnlyList<GraphSchemaEdge> edges)
    {
        canvas.Children.Clear();

        var edgeColors = _drawing.BuildEdgeColorMap(edges);
        var map = nodes.ToDictionary(n => n.Label);

        // Group by (from, to) so multiple edges between the same pair get distinct curvature offsets
        foreach (var grp in edges.GroupBy(e => (e.From, e.To)))
        {
            if (!map.TryGetValue(grp.Key.From, out var from)) continue;
            if (!map.TryGetValue(grp.Key.To, out var to)) continue;

            var labels = grp.Select(e => e.EdgeLabel).ToList();
            var isSelf = grp.Key.From == grp.Key.To;

            for (var i = 0; i < labels.Count; i++)
            {
                // BuildEdgeColorMap covers all labels in edges, so the lookup will always succeed
                var brush = edgeColors.GetValueOrDefault(labels[i])
                            ?? new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6));

                if (isSelf)
                    _drawing.DrawSelfLoop(canvas, from.X, from.Y, labels[i], i, brush);
                else
                    _drawing.DrawEdge(canvas, from.X, from.Y, to.X, to.Y, labels[i],
                        (i - (labels.Count - 1) / 2.0) * 50, brush);
            }
        }

        // Draw nodes on top so they cover edge endpoints
        foreach (var node in nodes)
            _drawing.DrawNode(canvas, node.X, node.Y, node.Label);
    }
}