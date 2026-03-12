using System.Windows.Media;

namespace GremlinQ.Rendering;

public interface IColorPalette
{
    Color[] EdgeColors { get; }
    SolidColorBrush NodeBorderBrush { get; }
    SolidColorBrush NodeBackgroundBrush { get; }
    SolidColorBrush NodeForegroundBrush { get; }
    SolidColorBrush EdgeLabelBackgroundBrush { get; }
}