using System.Windows;
using GremlinQ.Layout;
using GremlinQ.Rendering;
using GremlinQ.Services;
using GremlinQ.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace GremlinQ;

public partial class App : Application
{
    private ServiceProvider _serviceProvider = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        services.AddSingleton<IColorPalette, DefaultColorPalette>();
        services.AddSingleton<CanvasDrawingHelper>();
        services.AddSingleton<IGremlinConnectionService, GremlinConnectionService>();
        services.AddSingleton<IConnectionProfileRepository, ConnectionProfileRepository>();
        services.AddSingleton<IGremlinQueryService, GremlinQueryService>();
        services.AddSingleton<IGraphSchemaService, GraphSchemaService>();
        services.AddSingleton<IQueryHistoryManager, QueryHistoryManager>();
        services.AddSingleton<ForceDirectedLayoutEngine>();
        services.AddSingleton<GraphCanvasRenderer>();
        services.AddSingleton<RelationsCanvasRenderer>();
        services.AddTransient<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        _serviceProvider.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider.Dispose();
        base.OnExit(e);
    }
}
