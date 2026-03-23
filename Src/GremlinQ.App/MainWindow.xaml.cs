using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GremlinQ.App.Layout;
using GremlinQ.App.Models;
using GremlinQ.App.Rendering;
using GremlinQ.Core.Abstractions;
using GremlinQ.Core.Models;

namespace GremlinQ.App;

/// <summary>Interaction logic for MainWindow.xaml — UI coordination only.</summary>
public partial class MainWindow : Window
{
    // ── WPF command ───────────────────────────────────────────────────────────
    public static readonly RoutedCommand RunQueryCommand = new(nameof(RunQueryCommand), typeof(MainWindow));
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    // ── Canvas pan/zoom state ─────────────────────────────────────────────────
    private readonly MatrixTransform _canvasTransform = new();

    // ── Injected services ─────────────────────────────────────────────────────
    private readonly IConnectionProfileRepository _connectionProfileRepository;
    private readonly IGremlinConnectionService _connectionService;
    private readonly IGraphLayoutRepository _layoutRepository;
    private readonly List<GraphSchemaEdge> _graphEdges = [];

    // ── Graph tab state ───────────────────────────────────────────────────────
    private readonly List<GraphNode> _graphNodes = [];
    private readonly GraphCanvasRenderer _graphRenderer;

    private readonly ForceDirectedLayoutEngine _layoutEngine;
    private readonly IQueryHistoryManager _queryHistoryManager;
    private readonly IGremlinQueryService _queryService;
    private readonly List<GraphSchemaEdge> _relationsGraphEdges = [];
    private readonly RelationsCanvasRenderer _relationsRenderer;

    private readonly MatrixTransform _relationsTransform = new();
    private readonly IGraphSchemaService _schemaService;
    private GraphNode? _dragNode;
    private Point _dragOffset;
    private Point _graphClickStart;
    private bool _isPanning;
    private FrameworkElement? _pendingTaggedElement;
    private bool _isRelationsPanning;
    private bool _queryExpanded = true;
    private double _queryPanelWidth = double.NaN; // NaN = use star sizing until first collapse
    private Point _panStart;
    private Point _relationsPanStart;

    // ─────────────────────────────────────────────────────────────────────────
    // Construction
    // ─────────────────────────────────────────────────────────────────────────

    public MainWindow(
        IConnectionProfileRepository connectionProfileRepository,
        IGremlinConnectionService connectionService,
        IGremlinQueryService queryService,
        IGraphSchemaService schemaService,
        IQueryHistoryManager queryHistoryManager,
        IGraphLayoutRepository layoutRepository,
        ForceDirectedLayoutEngine layoutEngine,
        GraphCanvasRenderer graphRenderer,
        RelationsCanvasRenderer relationsRenderer)
    {
        _connectionProfileRepository = connectionProfileRepository;
        _connectionService = connectionService;
        _queryService = queryService;
        _schemaService = schemaService;
        _queryHistoryManager = queryHistoryManager;
        _layoutRepository = layoutRepository;
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
        RefreshConnectionsList();
    }

    private void RefreshConnectionsList()
    {
        var profiles = _connectionProfileRepository.LoadAll();
        CboEnvironment.ItemsSource = profiles;
        TxtNoConnections.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Connection management
    // ─────────────────────────────────────────────────────────────────────────

    private void BtnAddConnection_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddEditConnectionWindow();
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            _connectionProfileRepository.Save(dialog.Result);
            RefreshConnectionsList();
            CboEnvironment.SelectedItem = CboEnvironment.Items
                .Cast<ConnectionProfile>()
                .FirstOrDefault(p => p.Id == dialog.Result.Id);
        }
    }

    private void BtnEditConnection_Click(object sender, RoutedEventArgs e)
    {
        if (CboEnvironment.SelectedItem is not ConnectionProfile selected) return;

        var dialog = new AddEditConnectionWindow(selected);
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            _connectionProfileRepository.Save(dialog.Result);
            RefreshConnectionsList();
            CboEnvironment.SelectedItem = CboEnvironment.Items
                .Cast<ConnectionProfile>()
                .FirstOrDefault(p => p.Id == dialog.Result.Id);
        }
    }

    private void BtnDeleteConnection_Click(object sender, RoutedEventArgs e)
    {
        if (CboEnvironment.SelectedItem is not ConnectionProfile selected) return;

        var confirm = MessageBox.Show(
            $"Delete '{selected.Name}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm == MessageBoxResult.Yes)
        {
            _connectionService.Disconnect();
            SetStatus(ConnectionStatus.Disconnected);
            _connectionProfileRepository.Delete(selected.Id);
            RefreshConnectionsList();
        }
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
            _connectionService.Connect(profile);
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

    private void BtnCollapseQuery_Click(object sender, RoutedEventArgs e)
    {
        _queryExpanded = !_queryExpanded;
        if (_queryExpanded)
        {
            QueryEditorContent.Visibility = Visibility.Visible;
            TxtQueryTitle.Visibility = Visibility.Visible;
            QueryColumn.Width = double.IsNaN(_queryPanelWidth)
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(_queryPanelWidth);
            SplitterColumn.Width = new GridLength(5);
            QuerySplitter.Visibility = Visibility.Visible;
        }
        else
        {
            _queryPanelWidth = QueryColumn.ActualWidth;
            QueryEditorContent.Visibility = Visibility.Collapsed;
            TxtQueryTitle.Visibility = Visibility.Collapsed;
            QueryColumn.Width = GridLength.Auto;
            SplitterColumn.Width = new GridLength(0);
            QuerySplitter.Visibility = Visibility.Collapsed;
        }
        BtnCollapseQuery.Content = _queryExpanded ? "◄" : "►";
        BtnCollapseQuery.ToolTip = _queryExpanded ? "Collapse query panel" : "Expand query panel";
    }

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
            TxtResults.Text = JsonSerializer.Serialize(result.Items, PrettyJson);
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
        _queryHistoryManager.Add(query);

        // Rebuild ComboBox without triggering SelectionChanged
        CboHistory.SelectionChanged -= CboHistory_SelectionChanged;
        CboHistory.ItemsSource = null;
        CboHistory.ItemsSource = _queryHistoryManager.Items;
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

        var el = FindTaggedElement(e.OriginalSource as DependencyObject);
        if (el is not null)
        {
            _ = ShowPropertiesPopupAsync((string)el.Tag, GetElementBottomCenterScreen(el));
            e.Handled = true;
            return;
        }

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

    private void BtnResetLayout_Click(object sender, RoutedEventArgs e)
    {
        if (CboEnvironment.SelectedItem is ConnectionProfile profile)
            _layoutRepository.Delete(profile.Id);
        _ = LoadGraphAsync();
    }

    private void SaveLayout()
    {
        if (CboEnvironment.SelectedItem is not ConnectionProfile profile) return;
        var positions = _graphNodes.ToDictionary(
            n => n.Label,
            n => new NodePosition(n.X, n.Y));
        _layoutRepository.Save(profile.Id, positions);
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

            _graphNodes.AddRange(schema.Nodes.Select(v => new GraphNode(v.Label)));
            _graphEdges.AddRange(schema.Edges);

            var w = GraphCanvas.ActualWidth > 50 ? GraphCanvas.ActualWidth : 900;
            var h = GraphCanvas.ActualHeight > 50 ? GraphCanvas.ActualHeight : 600;

            _layoutEngine.PlaceNodesCircular(_graphNodes, w, h);
            _layoutEngine.RunLayout(_graphNodes, _graphEdges, w, h);

            // Override positions for nodes that have been manually arranged before.
            if (CboEnvironment.SelectedItem is ConnectionProfile layoutProfile)
            {
                var saved = _layoutRepository.Load(layoutProfile.Id);
                foreach (var node in _graphNodes)
                    if (saved.TryGetValue(node.Label, out var pos))
                    { node.X = pos.X; node.Y = pos.Y; }
            }

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
            _graphClickStart = pos;
            GraphCanvas.CaptureMouse();
        }
        else
        {
            // Always start pan; defer popup to MouseUp so drag is never blocked
            _pendingTaggedElement = FindTaggedElement(e.OriginalSource as DependencyObject);
            _isPanning = true;
            _panStart = e.GetPosition(GraphBorder);
            _graphClickStart = _panStart;
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
        if (_dragNode is not null)
        {
            var pos = e.GetPosition(GraphCanvas);
            if ((pos - _graphClickStart).Length < 5.0)
                _ = ShowPropertiesPopupAsync($"vertex:{_dragNode.Label}", GetNodeBottomCenterScreen(_dragNode));
            else
                SaveLayout();
        }
        else if (_pendingTaggedElement is not null)
        {
            var endPos = e.GetPosition(GraphBorder);
            if ((endPos - _graphClickStart).Length < 5.0)
                _ = ShowPropertiesPopupAsync((string)_pendingTaggedElement.Tag,
                    GetElementBottomCenterScreen(_pendingTaggedElement));
        }

        _dragNode = null;
        _pendingTaggedElement = null;
        _isPanning = false;
        GraphCanvas.ReleaseMouseCapture();
    }

    private void GraphCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        const double factor = 1.12;
        var scale = e.Delta > 0 ? factor : 1.0 / factor;
        // ScaleAt center must be in canvas-local space (the transform's input space).
        // GetPosition(GraphCanvas) correctly maps screen coords to that space.
        var pos = e.GetPosition(GraphCanvas);
        var m = _canvasTransform.Matrix;
        m.ScaleAt(scale, scale, pos.X, pos.Y);
        _canvasTransform.Matrix = m;
        e.Handled = true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Walks up the visual tree from <paramref name="source"/> to find the nearest element
    /// tagged "vertex:…" or "edge:…". Returns the element so callers can compute its screen position.</summary>
    private static FrameworkElement? FindTaggedElement(DependencyObject? source)
    {
        var el = source as FrameworkElement;
        while (el is not null)
        {
            if (el.Tag is string tag &&
                (tag.StartsWith("vertex:", StringComparison.Ordinal) ||
                 tag.StartsWith("edge:", StringComparison.Ordinal)))
                return el;
            el = VisualTreeHelper.GetParent(el) as FrameworkElement;
        }
        return null;
    }

    /// <summary>Returns the screen coordinates of the bottom-centre of a FrameworkElement,
    /// accounting for any RenderTransforms on ancestor canvases.</summary>
    private Point GetElementBottomCenterScreen(FrameworkElement element)
    {
        var winPoint = element.TranslatePoint(new Point(element.ActualWidth / 2, element.ActualHeight), this);
        return PointToScreen(winPoint);
    }

    /// <summary>Returns the screen coordinates of the bottom-centre of a graph node,
    /// mapping through the GraphCanvas pan/zoom RenderTransform.</summary>
    private Point GetNodeBottomCenterScreen(GraphNode node)
    {
        var canvasPoint = new Point(node.X, node.Y + CanvasDrawingHelper.NodeH / 2);
        var winPoint = GraphCanvas.TranslatePoint(canvasPoint, this);
        return PointToScreen(winPoint);
    }

    // Popup width (matches Width="240" on the card Border in XAML).
    // Arrow (18px wide) is centred in 240px → tip at x=120 in popup space.
    private const double PopupCardWidth = 240;

    private async Task ShowPropertiesPopupAsync(string tag, Point itemBottomCenter)
    {
        var colon = tag.IndexOf(':');
        var kind = tag[..colon];
        var label = tag[(colon + 1)..];

        TxtPopupHeader.Text = $"{(kind == "vertex" ? "Vertex" : "Edge")}  ·  {label}";
        TxtPopupStatus.Text = _connectionService.IsConnected ? "Loading…" : "Not connected";
        LstPopupProperties.ItemsSource = null;

        // Centre the popup horizontally on the item; place it just below.
        PropertiesPopup.HorizontalOffset = itemBottomCenter.X - PopupCardWidth / 2;
        PropertiesPopup.VerticalOffset = itemBottomCenter.Y + 2;
        PropertiesPopup.IsOpen = true;

        if (!_connectionService.IsConnected) return;

        try
        {
            var props = kind == "vertex"
                ? await _schemaService.LoadVertexPropertiesAsync(new VertexItem(label))
                : await _schemaService.LoadEdgePropertiesAsync(label);

            TxtPopupStatus.Text = props.Count > 0 ? string.Empty : "(no properties)";
            LstPopupProperties.ItemsSource = props.Count > 0 ? props : null;
        }
        catch
        {
            TxtPopupStatus.Text = "(error loading properties)";
        }
    }

    private void BtnClosePopup_Click(object sender, RoutedEventArgs e)
    {
        PropertiesPopup.IsOpen = false;
    }

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