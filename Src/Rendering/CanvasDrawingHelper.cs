using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GremlinQ.Models;
using WpfPath = System.Windows.Shapes.Path;

namespace GremlinQ.Rendering;

/// <summary>
///     Low-level WPF drawing primitives (edges, self-loops, nodes) shared by both canvas renderers.
/// </summary>
public sealed class CanvasDrawingHelper
{
    public const double NodeW = 130;
    public const double NodeH = 36;

    private readonly IColorPalette _palette;

    public CanvasDrawingHelper(IColorPalette palette)
    {
        _palette = palette;
    }

    // ── Edge colour map ───────────────────────────────────────────────────────

    /// <summary>
    ///     Builds a deterministic label→brush map by sorting labels alphabetically,
    ///     so the same edge label always receives the same colour across renders.
    /// </summary>
    public Dictionary<string, SolidColorBrush> BuildEdgeColorMap(
        IEnumerable<GraphSchemaEdge> edges)
    {
        return edges
            .Select(e => e.EdgeLabel)
            .Distinct()
            .OrderBy(l => l, StringComparer.Ordinal)
            .Select((label, i) => (label,
                brush: new SolidColorBrush(_palette.EdgeColors[i % _palette.EdgeColors.Length])))
            .ToDictionary(x => x.label, x => x.brush);
    }

    // ── Primitives ────────────────────────────────────────────────────────────

    public void DrawEdge(Canvas canvas,
        double x1, double y1, double x2, double y2,
        string label, double curvature, SolidColorBrush edgeBrush)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < 1) return;

        var ux = dx / dist;
        var uy = dy / dist; // unit along edge
        var px = -uy;
        var py = ux; // unit perpendicular

        const double margin = NodeH / 2 + 2;
        var sx = x1 + ux * margin;
        var sy = y1 + uy * margin; // start
        var ex = x2 - ux * margin;
        var ey = y2 - uy * margin; // end
        var cpx = (sx + ex) / 2 + px * curvature; // bezier control
        var cpy = (sy + ey) / 2 + py * curvature;

        // Curved line
        var fig = new PathFigure { StartPoint = new Point(sx, sy), IsClosed = false };
        fig.Segments.Add(new QuadraticBezierSegment(new Point(cpx, cpy), new Point(ex, ey), true));
        canvas.Children.Add(new WpfPath
        {
            Data = new PathGeometry { Figures = { fig } },
            Stroke = edgeBrush,
            StrokeThickness = 1.5
        });

        // Arrowhead: tangent at end = direction (end − control)
        var tx = ex - cpx;
        var ty = ey - cpy;
        var tl = Math.Sqrt(tx * tx + ty * ty);
        if (tl > 0.01)
        {
            tx /= tl;
            ty /= tl;
            const double a = 9.0, w = 3.8;
            canvas.Children.Add(new Polygon
            {
                Points = new PointCollection
                {
                    new Point(ex, ey),
                    new Point(ex - a * tx + w * -ty, ey - a * ty + w * tx),
                    new Point(ex - a * tx - w * -ty, ey - a * ty - w * tx)
                },
                Fill = edgeBrush,
                StrokeThickness = 0
            });
        }

        // Label at bezier midpoint (t = 0.5)
        var lx = 0.25 * sx + 0.5 * cpx + 0.25 * ex;
        var ly = 0.25 * sy + 0.5 * cpy + 0.25 * ey;
        var txt = new TextBlock
        {
            Text = label,
            Foreground = edgeBrush,
            Background = _palette.EdgeLabelBackgroundBrush,
            FontSize = 10,
            Padding = new Thickness(2, 0, 2, 0)
        };
        Canvas.SetLeft(txt, lx - 28);
        Canvas.SetTop(txt, ly - 8);
        canvas.Children.Add(txt);
    }

    public void DrawSelfLoop(Canvas canvas, double nodeCx, double nodeCy,
        string label, int index, SolidColorBrush edgeBrush)
    {
        var ox = (index - 0.5) * 28;
        var cx = nodeCx + ox;
        var cy = nodeCy - NodeH / 2;

        var fig = new PathFigure { StartPoint = new Point(cx - 16, cy), IsClosed = false };
        fig.Segments.Add(new BezierSegment(
            new Point(cx - 42, cy - 55),
            new Point(cx + 42, cy - 55),
            new Point(cx + 16, cy), true));
        canvas.Children.Add(new WpfPath
        {
            Data = new PathGeometry { Figures = { fig } },
            Stroke = edgeBrush,
            StrokeThickness = 1.5
        });

        var txt = new TextBlock
        {
            Text = label,
            Foreground = edgeBrush,
            Background = _palette.EdgeLabelBackgroundBrush,
            FontSize = 10,
            Padding = new Thickness(2, 0, 2, 0)
        };
        Canvas.SetLeft(txt, cx - 28);
        Canvas.SetTop(txt, cy - 68 - index * 16);
        canvas.Children.Add(txt);
    }

    public void DrawNode(Canvas canvas, double x, double y, string label, bool isSelected = false)
    {
        var border = new Border
        {
            Width = NodeW,
            Height = NodeH,
            Background = _palette.NodeBackgroundBrush,
            BorderBrush = isSelected
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00))
                : _palette.NodeBorderBrush,
            BorderThickness = new Thickness(isSelected ? 2.5 : 1.5),
            CornerRadius = new CornerRadius(18),
            Cursor = Cursors.SizeAll,
            Child = new TextBlock
            {
                Text = label,
                Foreground = _palette.NodeForegroundBrush,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        Canvas.SetLeft(border, x - NodeW / 2);
        Canvas.SetTop(border, y - NodeH / 2);
        canvas.Children.Add(border);
    }
}