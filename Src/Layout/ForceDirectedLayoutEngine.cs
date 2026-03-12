using Gremlinq.Models;

namespace Gremlinq.Layout;

/// <summary>
///     Places graph nodes using a force-directed simulation:
///     repulsion between all pairs, spring attraction along edges,
///     and a gentle gravity toward the canvas centre.
/// </summary>
public sealed class ForceDirectedLayoutEngine
{
    /// <summary>Arranges nodes evenly on a circle as the initial layout before simulation.</summary>
    public void PlaceNodesCircular(IList<GraphNode> nodes, double width, double height)
    {
        var n = nodes.Count;
        var r = Math.Min(width, height) * Math.Clamp(0.30 + n * 0.012, 0.30, 0.44);

        for (var i = 0; i < n; i++)
        {
            var a = 2 * Math.PI * i / Math.Max(n, 1) - Math.PI / 2;
            nodes[i].X = width / 2 + r * Math.Cos(a);
            nodes[i].Y = height / 2 + r * Math.Sin(a);
        }
    }

    /// <summary>Runs the force simulation for the given number of iterations, mutating node positions.</summary>
    public void RunLayout(IList<GraphNode> nodes, IReadOnlyList<GraphSchemaEdge> edges,
        double width, double height, int iterations = 250)
    {
        if (nodes.Count == 0) return;

        var n = nodes.Count;

        // Adaptive parameters scale with the number of nodes
        var repulsion = Math.Max(6000, 1800.0 * n);
        var springK = 0.04;
        var restLen = Math.Clamp(160 + n * 10, 150, 340.0);
        var gravity = 0.018;
        var decay = 0.87;

        var vx = new double[n];
        var vy = new double[n];

        // Build an index map once so spring-pair lookups are O(1)
        var indexMap = nodes
            .Select((node, i) => (node.Label, i))
            .ToDictionary(x => x.Label, x => x.i);

        // Deduplicate spring pairs: multiple edges between the same nodes add only one spring
        var springPairs = edges
            .Where(e => e.From != e.To)
            .Select(e => (e.From, e.To))
            .Distinct()
            .Where(p => indexMap.ContainsKey(p.From) && indexMap.ContainsKey(p.To))
            .Select(p => (indexMap[p.From], indexMap[p.To]))
            .ToList();

        for (var iter = 0; iter < iterations; iter++)
        {
            // Cooling factor: starts at 1, converges toward 0 as layout stabilises
            var alpha = Math.Max(0.005, 1.0 - (double)iter / iterations);

            for (var i = 0; i < n; i++)
            {
                nodes[i].Fx = 0;
                nodes[i].Fy = 0;
            }

            // Repulsion — every pair
            for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
            {
                var a = nodes[i];
                var b = nodes[j];
                double dx = a.X - b.X, dy = a.Y - b.Y;
                var d2 = dx * dx + dy * dy + 1;
                var d = Math.Sqrt(d2);
                var f = repulsion / d2;
                a.Fx += f * dx / d;
                a.Fy += f * dy / d;
                b.Fx -= f * dx / d;
                b.Fy -= f * dy / d;
            }

            // Spring attraction — one spring per unique node pair
            foreach (var (ai, bi) in springPairs)
            {
                var a = nodes[ai];
                var b = nodes[bi];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                var d = Math.Sqrt(dx * dx + dy * dy) + 0.01;
                var f = springK * (d - restLen);
                a.Fx += f * dx / d;
                a.Fy += f * dy / d;
                b.Fx -= f * dx / d;
                b.Fy -= f * dy / d;
            }

            // Gravity — keeps disconnected sub-graphs from drifting away
            for (var i = 0; i < n; i++)
            {
                nodes[i].Fx += gravity * (width / 2 - nodes[i].X);
                nodes[i].Fy += gravity * (height / 2 - nodes[i].Y);
            }

            // Velocity integration with cooling and damping
            for (var i = 0; i < n; i++)
            {
                vx[i] = (vx[i] + nodes[i].Fx * alpha) * decay;
                vy[i] = (vy[i] + nodes[i].Fy * alpha) * decay;
                nodes[i].X += vx[i];
                nodes[i].Y += vy[i];
            }
        }

        // Re-centre: translate all nodes so their centroid sits at the canvas centre
        var cx = nodes.Average(node => node.X);
        var cy = nodes.Average(node => node.Y);
        var ox = width / 2 - cx;
        var oy = height / 2 - cy;
        foreach (var node in nodes)
        {
            node.X += ox;
            node.Y += oy;
        }
    }
}