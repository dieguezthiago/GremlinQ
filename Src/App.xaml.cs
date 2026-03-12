using System.Windows;
using GremlinQ.Layout;
using GremlinQ.Rendering;
using GremlinQ.Services;

namespace GremlinQ;

/// <summary>Application entry point — wires all dependencies and creates the main window.</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var palette = new DefaultColorPalette();
        var drawingHelper = new CanvasDrawingHelper(palette);
        var connectionService = new GremlinConnectionService();
        var profileRepository = new ConnectionProfileRepository();
        var queryService = new GremlinQueryService(connectionService);
        var schemaService = new GraphSchemaService(connectionService);
        var historyManager = new QueryHistoryManager();
        var layoutEngine = new ForceDirectedLayoutEngine();
        var graphRenderer = new GraphCanvasRenderer(drawingHelper);
        var relationsRenderer = new RelationsCanvasRenderer(drawingHelper);

        var mainWindow = new MainWindow(
            profileRepository,
            connectionService,
            queryService,
            schemaService,
            historyManager,
            layoutEngine,
            graphRenderer,
            relationsRenderer);

        mainWindow.Show();
    }
}