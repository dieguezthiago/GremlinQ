namespace GremlinQ.App.Models;

/// <summary>Mutable node whose position is updated by the force layout and interactive dragging.</summary>
public sealed class GraphNode(string label)
{
    public string Label { get; } = label;
    public double X { get; set; }
    public double Y { get; set; }
    public double Fx { get; set; } // accumulated force
    public double Fy { get; set; }
}