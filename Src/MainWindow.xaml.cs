using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Gremlinq.Layout;
using Gremlinq.Models;
using Gremlinq.Rendering;
using Gremlinq.Services.Interfaces;
using Path = System.IO.Path;

namespace Gremlinq;

/// <summary>Interaction logic for MainWindow.xaml — UI coordination only.</summary>
public partial class MainWindow : Window
{
    // ── WPF command ───────────────────────────────────────────────────────────
    public static readonly RoutedCommand RunQueryCommand = new(nameof(RunQueryCommand), typeof(MainWindow));
    private static readonly JsonSerializerOptions _prettyJson = new() { WriteIndented = true };

    // ── Canvas pan/zoom state ─────────────────────────────────────────────────
    private readonly MatrixTransform _canvasTransform = new();
    private readonly IGremlinConnectionService _connectionService;
    private readonly List<GraphSchemaEdge> _graphEdges = [];

    // ── Graph tab state ───────────────────────────────────────────────────────
    private readonly List<GraphNode> _graphNodes = [];
    private readonly GraphCanvasRenderer _graphRenderer;
    private readonly IQueryHistoryManager _historyManager;

    private readonly ForceDirectedLayoutEngine _layoutEngine;

    // ── Injected services ─────────────────────────────────────────────────────
    private readonly IConnectionProfileRepository _profileRepository;
    private readonly IGremlinQueryService _queryService;
    private readonly List<GraphSchemaEdge> _relationsGraphEdges = [];
    private readonly RelationsCanvasRenderer _relationsRenderer;

    private readonly MatrixTransform _relationsTransform = new();
    private readonly IGraphSchemaService _schemaService;
    private GraphNode? _dragNode;
    private Point _dragOffset;
    private bool _isPanning;
    private bool _isRelationsPanning;
    private Point _panStart;
    private Point _relationsPanStart;

    // ─────────────────────────────────────────────────────────────────────────
    // Construction
    // ─────────────────────────────────────────────────────────────────────────

    public MainWindow(
        IConnectionProfileRepository profileRepository,
        IGremlinConnectionService connectionService,
        IGremlinQueryService queryService,
        IGraphSchemaService schemaService,
        IQueryHistoryManager historyManager,
        ForceDirectedLayoutEngine layoutEngine,
        GraphCanvasRenderer graphRenderer,
        RelationsCanvasRenderer relationsRenderer)
    {
        _profileRepository = profileRepository;
        _connectionService = connectionService;
        _queryService = queryService;
        _schemaService = schemaService;
        _historyManager = historyManager;
        _layoutEngine = layoutEngine;
        _graphRenderer = graphRenderer;
        _relationsRenderer = relationsRenderer;

        InitializeComponent();

        CommandBindings.Add(new CommandBinding(RunQueryCommand, (_, _) => _ = ExecuteQueryAsync()));

        GraphCanvas.RenderTransform = _canvasTransform;
        RelationsCanvas.RenderTransform = _relationsTransform;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Startup
    // ─────────────────────────────────────────────────────────────────────────

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var connectionsFolder = Path.Combine(AppContext.BaseDirectory, "connections");
        var profiles = _profileRepository.Load(connectionsFolder);

        CboEnvironment.ItemsSource = profiles;

        if (profiles.Count > 0)
            CboEnvironment.SelectedIndex = 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Environment selection
    // ─────────────────────────────────────────────────────────────────────────

    private void CboEnvironment_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _connectionService.Disconnect();
        SetStatus(ConnectionStatus.Disconnected);

        if (CboEnvironment.SelectedItem is not ConnectionProfile profile)
            return;

        TxtHost.Text = profile.Host;
        TxtPort.Text = profile.Port.ToString();
        TxtDatabase.Text = profile.Database;
        TxtCollection.Text = profile.Collection;
        PwdKey.Password = profile.Key;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Connect
    // ─────────────────────────────────────────────────────────────────────────

    private void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        Connect();
    }

    private void Connect()
    {
        if (CboEnvironment.SelectedItem is not ConnectionProfile profile)
        {
            ShowError("No environment selected.");
            return;
        }

        _connectionService.Disconnect();
        SetStatus(ConnectionStatus.Connecting);
        BtnConnect.IsEnabled = false;

        try
        {
            _connectionService.Connect(profile, PwdKey.Password);
            SetStatus(ConnectionStatus.Connected);
            TxtStatus.Text = $"Connected to {profile.Name}";
        }
        catch (Exception ex)
        {
            _connectionService.Disconnect();
            SetStatus(ConnectionStatus.Error);
            ShowError($"Connection failed: {ex.Message}");
        }
        finally
        {
            BtnConnect.IsEnabled = true;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Query execution
    // ─────────────────────────────────────────────────────────────────────────

    private void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        _ = ExecuteQueryAsync();
    }

    private void TxtQuery_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            e.Handled = true;
            _ = ExecuteQueryAsync();
        }
    }

    private async Task ExecuteQueryAsync()
    {
        if (!_connectionService.IsConnected)
        {
            ShowError("Not connected. Click Connect first.");
            return;
        }

        var query = TxtQuery.Text.Trim();
        if (string.IsNullOrEmpty(query)) return;

        BtnRun.IsEnabled = false;
        TxtResults.Text = string.Empty;
        TxtStatus.Text = "Running…";

        try
        {
            var result = await _queryService.ExecuteAsync(query);
            TxtResults.Text = JsonSerializer.Serialize(result.Items, _prettyJson);
            TxtStatus.Text = $"{result.Items.Count} result(s)  ·  {result.ElapsedMs} ms  ·  {result.ProfileName}";
            AddToHistory(query);
        }
        catch (Exception ex)
        {
            ShowError($"Query error: {ex.Message}");
        }
        finally
        {
            BtnRun.IsEnabled = true;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Query shortcuts
    // ─────────────────────────────────────────────────────────────────────────

    private void BtnShortcutListVertices_Click(object sender, RoutedEventArgs e)
    {
        RunShortcut("g.V().label().dedup()");
    }

    private void BtnShortcutListEdges_Click(object sender, RoutedEventArgs e)
    {
        RunShortcut("g.E().label().dedup()");
    }

    private void RunShortcut(string query)
    {
        TxtQuery.Text = query;
        _ = ExecuteShortcutAsync(query);
    }

    private async Task ExecuteShortcutAsync(string query)
    {
        if (!_connectionService.IsConnected)
        {
            ShowError("Not connected. Click Connect first.");
            return;
        }

        BtnRun.IsEnabled = false;
        TxtResults.Text = string.Empty;
        TxtStatus.Text = "Running…";

        try
        {
            var result = await _queryService.ExecuteAsync(query);

            var labels = result.Items
                .Select(r => r?.ToString() ?? string.Empty)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            TxtResults.Text = string.Join(Environment.NewLine, labels);
            TxtStatus.Text = $"{labels.Count} result(s)  ·  {result.ElapsedMs} ms  ·  {result.ProfileName}";
        }
        catch (Exception ex)
        {
            ShowError($"Query error: {ex.Message}");
        }
        finally
        {
            BtnRun.IsEnabled = true;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Clear buttons
    // ─────────────────────────────────────────────────────────────────────────

    private void BtnClearResults_Click(object sender, RoutedEventArgs e)
    {
        TxtResults.Text = string.Empty;
        TxtStatus.Text = "Ready";
    }

    private void BtnClearQuery_Click(object sender, RoutedEventArgs e)
    {
        TxtQuery.Text = string.Empty;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Query history
    // ─────────────────────────────────────────────────────────────────────────

    private void CboHistory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboHistory.SelectedItem is HistoryEntry entry)
            TxtQuery.Text = entry.Query;
    }

    private void AddToHistory(string query)
    {
        _historyManager.Add(query);

        // Rebuild ComboBox without triggering SelectionChanged
        CboHistory.SelectionChanged -= CboHistory_SelectionChanged;
        CboHistory.ItemsSource = null;
        CboHistory.ItemsSource = _historyManager.Items;
        CboHistory.SelectedIndex = 0;
        CboHistory.SelectionChanged += CboHistory_SelectionChanged;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Relations tab
    // ─────────────────────────────────────────────────────────────────────────

    private void BtnLoadVertices_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadVerticesAsync();
    }

    private void LstVertices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LstEdges.ItemsSource = null;
        LstDestVertices.ItemsSource = null;
        _relationsGraphEdges.Clear();
        RelationsCanvas.Children.Clear();
        _relationsTransform.Matrix = Matrix.Identity;

        if (LstVertices.SelectedItem is VertexItem vertex)
        {
            _ = LoadEdgesAsync(vertex);
            _ = LoadRelationsEdgesAsync(vertex);
        }
    }

    private async Task LoadVerticesAsync()
    {
        if (!_connectionService.IsConnected)
        {
            TxtRelationsStatus.Text = "Not connected — click Connect first.";
            return;
        }

        BtnLoadVertices.IsEnabled = false;
        LstVertices.ItemsSource = null;
        LstEdges.ItemsSource = null;
        LstDestVertices.ItemsSource = null;
        TxtRelationsStatus.Text = "Loading…";

        try
        {
            var vertices = await _schemaService.LoadVerticesAsync();
            LstVertices.ItemsSource = vertices;
            TxtRelationsStatus.Text = $"{vertices.Count} vertices";
        }
        catch (Exception ex)
        {
            TxtRelationsStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            BtnLoadVertices.IsEnabled = true;
        }
    }

    private async Task LoadEdgesAsync(VertexItem vertex)
    {
        if (!_connectionService.IsConnected) return;

        TxtRelationsStatus.Text = $"Loading edge types for {vertex.Label}…";

        try
        {
            var edges = await _schemaService.LoadEdgesAsync(vertex);
            LstEdges.ItemsSource = edges;
            TxtRelationsStatus.Text = $"{vertex.Label}  ·  {edges.Count} edge type(s)";
        }
        catch (Exception ex)
        {
            TxtRelationsStatus.Text = $"Error: {ex.Message}";
        }
    }

    private void LstEdges_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LstDestVertices.ItemsSource = null;

        if (LstVertices.SelectedItem is VertexItem srcVertex &&
            LstEdges.SelectedItem is EdgeLabelItem edgeLabel)
            _ = LoadDestVerticesAsync(srcVertex, edgeLabel);

        RenderRelationsGraph();
    }

    private void LstDestVertices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderRelationsGraph();
    }

    private async Task LoadDestVerticesAsync(VertexItem srcVertex, EdgeLabelItem edgeLabel)
    {
        if (!_connectionService.IsConnected) return;

        var arrow = edgeLabel.IsOutgoing ? "→" : "←";
        TxtRelationsStatus.Text = $"Loading connections for {srcVertex.Label} {arrow} {edgeLabel.Label}…";

        try
        {
            var destVertices = await _schemaService.LoadDestVerticesAsync(srcVertex, edgeLabel);
            LstDestVertices.ItemsSource = destVertices;
            TxtRelationsStatus.Text =
                $"{srcVertex.Label}  {arrow}  {edgeLabel.Label}  ·  {destVertices.Count} vertex type(s)";
        }
        catch (Exception ex)
        {
            TxtRelationsStatus.Text = $"Error: {ex.Message}";
        }
    }

    private async Task LoadRelationsEdgesAsync(VertexItem vertex)
    {
        if (!_connectionService.IsConnected) return;

        try
        {
            var edges = await _schemaService.LoadRelationsEdgesAsync(vertex);
            foreach (var edge in edges) _relationsGraphEdges.Add(edge);
        }
        catch
        {
            /* best-effort — preview panel is non-critical */
        }

        RenderRelationsGraph();
    }

    private void RenderRelationsGraph()
    {
        RelationsCanvas.Children.Clear();

        if (LstVertices.SelectedItem is not VertexItem srcVertex) return;

        var w = RelationsCanvas.ActualWidth > 50 ? RelationsCanvas.ActualWidth : 380;
        var h = RelationsCanvas.ActualHeight > 50 ? RelationsCanvas.ActualHeight : 400;

        var context = new RelationsRenderContext(
            srcVertex,
            _relationsGraphEdges,
            LstEdges.SelectedItem as EdgeLabelItem,
            LstDestVertices.SelectedItem as VertexItem,
            w,
            h);

        _relationsRenderer.Render(RelationsCanvas, context);
    }

    // ── Relations canvas — pan / zoom / toggle-selection ─────────────────────

    private void RelationsCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed && e.MiddleButton != MouseButtonState.Pressed)
            return;
        _isRelationsPanning = true;
        _relationsPanStart = e.GetPosition(RelationsPreviewBorder);
        RelationsCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void RelationsCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isRelationsPanning) return;
        if (e.LeftButton != MouseButtonState.Pressed && e.MiddleButton != MouseButtonState.Pressed)
            return;
        var current = e.GetPosition(RelationsPreviewBorder);
        var delta = current - _relationsPanStart;
        _relationsPanStart = current;
        var m = _relationsTransform.Matrix;
        m.Translate(delta.X, delta.Y);
        _relationsTransform.Matrix = m;
    }

    private void RelationsCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _isRelationsPanning = false;
        RelationsCanvas.ReleaseMouseCapture();
    }

    private void RelationsCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        const double factor = 1.12;
        var scale = e.Delta > 0 ? factor : 1.0 / factor;
        var pos = e.GetPosition(RelationsPreviewBorder);
        var m = _relationsTransform.Matrix;
        m.ScaleAt(scale, scale, pos.X, pos.Y);
        _relationsTransform.Matrix = m;
        e.Handled = true;
    }

    private static void ToggleListBoxSelection(ListBox lb, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var item = ItemsControl.ContainerFromElement(lb, e.OriginalSource as DependencyObject)
            as ListBoxItem;
        if (item?.IsSelected == true)
        {
            lb.SelectedItem = null;
            e.Handled = true;
        }
    }

    private void LstVertices_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        ToggleListBoxSelection(LstVertices, e);
    }

    private void LstEdges_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        ToggleListBoxSelection(LstEdges, e);
    }

    private void LstDestVertices_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        ToggleListBoxSelection(LstDestVertices, e);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Graph tab
    // ─────────────────────────────────────────────────────────────────────────

    private void BtnLoadGraph_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadGraphAsync();
    }

    private void BtnResetView_Click(object sender, RoutedEventArgs e)
    {
        _canvasTransform.Matrix = Matrix.Identity;
    }

    private async Task LoadGraphAsync()
    {
        if (!_connectionService.IsConnected)
        {
            TxtGraphStatus.Text = "Not connected — click Connect first.";
            return;
        }

        BtnLoadGraph.IsEnabled = false;
        TxtGraphStatus.Text = "Loading schema…";
        GraphCanvas.Children.Clear();
        _graphNodes.Clear();
        _graphEdges.Clear();
        _canvasTransform.Matrix = Matrix.Identity;

        try
        {
            var schema = await _schemaService.LoadFullSchemaAsync();

            _graphNodes.AddRange(schema.Nodes);
            _graphEdges.AddRange(schema.Edges);

            var w = GraphCanvas.ActualWidth > 50 ? GraphCanvas.ActualWidth : 900;
            var h = GraphCanvas.ActualHeight > 50 ? GraphCanvas.ActualHeight : 600;

            _layoutEngine.PlaceNodesCircular(_graphNodes, w, h);
            _layoutEngine.RunLayout(_graphNodes, _graphEdges, w, h);
            _graphRenderer.Render(GraphCanvas, _graphNodes, _graphEdges);

            var distinctEdgeTypes = _graphEdges.Select(e => e.EdgeLabel).Distinct().Count();
            TxtGraphStatus.Text = $"{_graphNodes.Count} vertex types  ·  {distinctEdgeTypes} edge types";
        }
        catch (Exception ex)
        {
            TxtGraphStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            BtnLoadGraph.IsEnabled = true;
        }
    }

    // ── Graph canvas — drag / pan / zoom ─────────────────────────────────────

    private void GraphCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.MiddleButton == MouseButtonState.Pressed)
        {
            _isPanning = true;
            _panStart = e.GetPosition(GraphBorder);
            GraphCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(GraphCanvas);
        _dragNode = _graphNodes.FirstOrDefault(n =>
            Math.Abs(n.X - pos.X) < CanvasDrawingHelper.NodeW / 2 + 2 &&
            Math.Abs(n.Y - pos.Y) < CanvasDrawingHelper.NodeH / 2 + 2);

        if (_dragNode is not null)
        {
            _dragOffset = new Point(pos.X - _dragNode.X, pos.Y - _dragNode.Y);
            GraphCanvas.CaptureMouse();
        }
        else
        {
            _isPanning = true;
            _panStart = e.GetPosition(GraphBorder);
            GraphCanvas.CaptureMouse();
        }

        e.Handled = true;
    }

    private void GraphCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragNode is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(GraphCanvas);
            _dragNode.X = pos.X - _dragOffset.X;
            _dragNode.Y = pos.Y - _dragOffset.Y;
            _graphRenderer.Render(GraphCanvas, _graphNodes, _graphEdges);
            return;
        }

        if (_isPanning && (e.LeftButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed))
        {
            var current = e.GetPosition(GraphBorder);
            var delta = current - _panStart;
            _panStart = current;
            var m = _canvasTransform.Matrix;
            m.Translate(delta.X, delta.Y);
            _canvasTransform.Matrix = m;
        }
    }

    private void GraphCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragNode = null;
        _isPanning = false;
        GraphCanvas.ReleaseMouseCapture();
    }

    private void GraphCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        const double factor = 1.12;
        var scale = e.Delta > 0 ? factor : 1.0 / factor;
        var pos = e.GetPosition(GraphBorder);
        var m = _canvasTransform.Matrix;
        m.ScaleAt(scale, scale, pos.X, pos.Y);
        _canvasTransform.Matrix = m;
        e.Handled = true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void ShowError(string message)
    {
        TxtResults.Text = $"ERROR\n\n{message}";
        TxtStatus.Text = "Error";
    }

    private void SetStatus(ConnectionStatus status)
    {
        (StatusIndicator.Fill, StatusIndicator.ToolTip) = status switch
        {
            ConnectionStatus.Connected => (new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10)), "Connected"),
            ConnectionStatus.Connecting => (new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x00)), "Connecting…"),
            ConnectionStatus.Error => (new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38)), "Connection error"),
            _ => (new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), "Not connected")
        };
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _connectionService.Disconnect();
    }

    private enum ConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Error
    }
}