using System.Windows.Controls;
using System.Windows.Media;
using GremlinQ.Models;

namespace GremlinQ.Rendering;

/// <summary>
///     Renders the Relations tab mini-graph: source vertex in the centre,
///     incoming vertices on the left, outgoing vertices on the right.
/// </summary>
public sealed class RelationsCanvasRenderer
{
    private readonly CanvasDrawingHelper _drawing;

    public RelationsCanvasRenderer(CanvasDrawingHelper drawing)
    {
        _drawing = drawing;
    }

    public void Render(Canvas canvas, RelationsRenderContext ctx)
    {
        canvas.Children.Clear();

        var w = ctx.Width;
        var h = ctx.Height;

        // ── Filter edges based on current selection depth ─────────────────────
        IEnumerable<GraphSchemaEdge> edges = ctx.AllSchemaEdges;

        if (ctx.SelectedEdge is not null)
            edges = ctx.SelectedEdge.IsOutgoing
                ? edges.Where(e => e.From == ctx.SourceVertex.Label && e.EdgeLabel == ctx.SelectedEdge.Label)
                : edges.Where(e => e.To == ctx.SourceVertex.Label && e.EdgeLabel == ctx.SelectedEdge.Label);

        if (ctx.SelectedDest is not null && ctx.SelectedEdge is not null)
            edges = ctx.SelectedEdge.IsOutgoing
                ? edges.Where(e => e.To == ctx.SelectedDest.Label)
                : edges.Where(e => e.From == ctx.SelectedDest.Label);

        var filtered = edges.ToList();

        // ── Side-node collections ─────────────────────────────────────────────
        // outNodes: vertex types that receive an edge FROM srcVertex (rendered right)
        var outNodes = filtered
            .Where(e => e.From == ctx.SourceVertex.Label && e.To != ctx.SourceVertex.Label)
            .Select(e => e.To).Distinct().ToList();

        // inNodes: vertex types that send an edge TO srcVertex (rendered left)
        var inNodes = filtered
            .Where(e => e.To == ctx.SourceVertex.Label && e.From != ctx.SourceVertex.Label)
            .Select(e => e.From).Distinct().ToList();

        // ── Stable colour map ─────────────────────────────────────────────────
        var edgeColors = _drawing.BuildEdgeColorMap(ctx.AllSchemaEdges);

        // ── Layout ────────────────────────────────────────────────────────────
        var cx = w / 2;
        var cy = h / 2;
        var rightX = w * 0.80;
        var leftX = w * 0.20;

        double Step(int count)
        {
            return count <= 1
                ? 0
                : Math.Min(CanvasDrawingHelper.NodeH + 18, (h - CanvasDrawingHelper.NodeH * 2.5) / (count - 1));
        }

        var outPos = outNodes
            .Select((label, i) => (label, x: rightX,
                y: cy + (i - (outNodes.Count - 1) / 2.0) * Step(outNodes.Count)))
            .ToList();

        var inPos = inNodes
            .Select((label, i) => (label, x: leftX,
                y: cy + (i - (inNodes.Count - 1) / 2.0) * Step(inNodes.Count)))
            .ToList();

        var outPosMap = outPos.ToDictionary(p => p.label, p => (p.x, p.y));
        var inPosMap = inPos.ToDictionary(p => p.label, p => (p.x, p.y));

        // ── Draw edges ────────────────────────────────────────────────────────
        foreach (var grp in filtered.Where(e => e.From != e.To).GroupBy(e => (e.From, e.To)))
        {
            var isOut = grp.Key.From == ctx.SourceVertex.Label;
            var labels = grp.Select(e => e.EdgeLabel).ToList();

            for (var i = 0; i < labels.Count; i++)
            {
                var brush = edgeColors.GetValueOrDefault(labels[i])
                            ?? new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6));
                var curv = (i - (labels.Count - 1) / 2.0) * 40;

                if (isOut && outPosMap.TryGetValue(grp.Key.To, out var tp))
                    _drawing.DrawEdge(canvas, cx, cy, tp.x, tp.y, labels[i], curv, brush);
                else if (!isOut && inPosMap.TryGetValue(grp.Key.From, out var fp))
                    _drawing.DrawEdge(canvas, fp.x, fp.y, cx, cy, labels[i], curv, brush);
            }
        }

        // Self-loops on the source vertex
        var selfEdges = filtered.Where(e => e.From == e.To).ToList();
        for (var i = 0; i < selfEdges.Count; i++)
        {
            var brush = edgeColors.GetValueOrDefault(selfEdges[i].EdgeLabel)
                        ?? new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6));
            _drawing.DrawSelfLoop(canvas, cx, cy, selfEdges[i].EdgeLabel, i, brush);
        }

        // ── Draw nodes (side first, centre on top) ────────────────────────────
        foreach (var (label, x, y) in outPos)
            _drawing.DrawNode(canvas, x, y, label,
                label == ctx.SelectedDest?.Label && ctx.SelectedEdge?.IsOutgoing == true);

        foreach (var (label, x, y) in inPos)
            _drawing.DrawNode(canvas, x, y, label,
                label == ctx.SelectedDest?.Label && ctx.SelectedEdge?.IsOutgoing == false);

        _drawing.DrawNode(canvas, cx, cy, ctx.SourceVertex.Label, true);
    }
}