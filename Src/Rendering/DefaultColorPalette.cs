using System.Windows.Media;

namespace Gremlinq.Rendering;

/// <summary>VS Code–inspired dark-theme colour palette for graph rendering.</summary>
public sealed class DefaultColorPalette : IColorPalette
{
    public Color[] EdgeColors { get; } =
    [
        Color.FromRgb(0x56, 0x9C, 0xD6), // blue
        Color.FromRgb(0x4E, 0xC9, 0xB0), // teal
        Color.FromRgb(0xDC, 0xDC, 0xAA), // yellow
        Color.FromRgb(0xC5, 0x86, 0xC0), // purple
        Color.FromRgb(0xCE, 0x91, 0x78), // orange
        Color.FromRgb(0x57, 0xA6, 0x4A), // green
        Color.FromRgb(0xF4, 0x4B, 0x4B), // red
        Color.FromRgb(0x9C, 0xDC, 0xFE) // light blue
    ];

    public SolidColorBrush NodeBorderBrush { get; } = new(Color.FromRgb(0x4E, 0xC9, 0xB0));
    public SolidColorBrush NodeBackgroundBrush { get; } = new(Color.FromRgb(0x2D, 0x2D, 0x30));
    public SolidColorBrush NodeForegroundBrush { get; } = new(Color.FromRgb(0x4E, 0xC9, 0xB0));
    public SolidColorBrush EdgeLabelBackgroundBrush { get; } = new(Color.FromRgb(0x1E, 0x1E, 0x1E));
}