using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Gremlin.Net.Driver;
using Path = System.IO.Path;
using WpfPath = System.Windows.Shapes.Path;

namespace Gremlinq;

/// <summary>Interaction logic for MainWindow.xaml.</summary>
public partial class MainWindow : Window
{
    private GremlinClient? _client;
    private ConnectionProfile? _activeProfile;

    private const int MaxHistoryItems = 50;
    private readonly List<HistoryEntry> _queryHistory = [];

    // ── Relations tab data models ──────────────────────────────────

    private sealed record VertexItem(string Label)
    {
        /// <summary>Gremlin-safe label reference for use in hasLabel().</summary>
        public string GremlinRef => $"'{Label.Replace("'", "\\'")}'";
    }

    private sealed record EdgeLabelItem(string Label, bool IsOutgoing)
    {
        public string GremlinRef => $"'{Label.Replace("'", "\\'")}'";
    }

    // ── History model ──────────────────────────────────────────────

    private sealed record HistoryEntry(string Query)
    {
        // Displayed in the ComboBox as a single line
        public override string ToString() =>
            Query.ReplaceLineEndings(" ").Trim();
    }

    private static readonly JsonSerializerOptions _prettyJson = new()
    {
        WriteIndented = true
    };

    /// <summary>Routed command bound to F5 for running the query.</summary>
    public static readonly RoutedCommand RunQueryCommand = new(
        nameof(RunQueryCommand), typeof(MainWindow));

    public MainWindow()
    {
        InitializeComponent();

        // Bind the F5 routed command
        CommandBindings.Add(new CommandBinding(RunQueryCommand, (_, _) => _ = ExecuteQueryAsync()));

        GraphCanvas.RenderTransform     = _canvasTransform;
        RelationsCanvas.RenderTransform = _relationsTransform;

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    // ──────────────────────────────────────────────────────────────
    // Startup
    // ──────────────────────────────────────────────────────────────

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var connectionsFolder = Path.Combine(AppContext.BaseDirectory, "connections");
        var profiles = ConnectionLoader.Load(connectionsFolder);

        CboEnvironment.ItemsSource = profiles;

        if (profiles.Count > 0)
            CboEnvironment.SelectedIndex = 0;
    }

    // ──────────────────────────────────────────────────────────────
    // Environment selection
    // ──────────────────────────────────────────────────────────────

    private void CboEnvironment_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DisposeClient();
        SetStatus(ConnectionStatus.Disconnected);

        if (CboEnvironment.SelectedItem is not ConnectionProfile profile)
            return;

        TxtHost.Text       = profile.Host;
        TxtPort.Text       = profile.Port.ToString();
        TxtDatabase.Text   = profile.Database;
        TxtCollection.Text = profile.Collection;

        PwdKey.Password = profile.Key;
    }

    // ──────────────────────────────────────────────────────────────
    // Connect
    // ──────────────────────────────────────────────────────────────

    private void BtnConnect_Click(object sender, RoutedEventArgs e)
        => Connect();

    private void Connect()
    {
        if (CboEnvironment.SelectedItem is not ConnectionProfile profile)
        {
            ShowError("No environment selected.");
            return;
        }

        DisposeClient();
        SetStatus(ConnectionStatus.Connecting);
        BtnConnect.IsEnabled = false;

        try
        {
            var key = PwdKey.Password;

            var server = new GremlinServer(
                hostname:  profile.Host,
                port:      profile.Port,
                enableSsl: profile.EnableSsl,
                username:  $"/dbs/{profile.Database}/colls/{profile.Collection}",
                password:  key);

            var connectionPoolSettings = new ConnectionPoolSettings
            {
                MaxInProcessPerConnection = 32,
                PoolSize                  = 4
            };

            _client = new GremlinClient(
                gremlinServer:     server,
                messageSerializer: new CosmosDbMessageSerializer(),
                connectionPoolSettings: connectionPoolSettings);

            _activeProfile = profile;
            SetStatus(ConnectionStatus.Connected);
            TxtStatus.Text = $"Connected to {profile.Name}";
        }
        catch (Exception ex)
        {
            DisposeClient();
            SetStatus(ConnectionStatus.Error);
            ShowError($"Connection failed: {ex.Message}");
        }
        finally
        {
            BtnConnect.IsEnabled = true;
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Query execution
    // ──────────────────────────────────────────────────────────────

    private void BtnRun_Click(object sender, RoutedEventArgs e)
        => _ = ExecuteQueryAsync();

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
        if (_client is null)
        {
            ShowError("Not connected. Click Connect first.");
            return;
        }

        var query = TxtQuery.Text.Trim();
        if (string.IsNullOrEmpty(query))
            return;

        BtnRun.IsEnabled = false;
        TxtResults.Text  = string.Empty;
        TxtStatus.Text   = "Running…";

        var sw = Stopwatch.StartNew();
        try
        {
            var results = await _client.SubmitAsync<dynamic>(query);
            sw.Stop();

            var resultList = results.ToList();
            var json = JsonSerializer.Serialize(resultList, _prettyJson);

            TxtResults.Text = json;
            TxtStatus.Text  = $"{resultList.Count} result(s)  ·  {sw.ElapsedMilliseconds} ms  ·  {_activeProfile?.Name ?? "unknown"}";
            AddToHistory(query);
        }
        catch (Exception ex)
        {
            sw.Stop();
            ShowError($"Query error ({sw.ElapsedMilliseconds} ms): {ex.Message}");
        }
        finally
        {
            BtnRun.IsEnabled = true;
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Query shortcuts
    // ──────────────────────────────────────────────────────────────

    private void BtnShortcutListVertices_Click(object sender, RoutedEventArgs e)
        => RunShortcut("g.V().label().dedup()");

    private void BtnShortcutListEdges_Click(object sender, RoutedEventArgs e)
        => RunShortcut("g.E().label().dedup()");

    private void RunShortcut(string query)
    {
        TxtQuery.Text = query;
        _ = ExecuteShortcutAsync(query);
    }

    private async Task ExecuteShortcutAsync(string query)
    {
        if (_client is null)
        {
            ShowError("Not connected. Click Connect first.");
            return;
        }

        BtnRun.IsEnabled = false;
        TxtResults.Text  = string.Empty;
        TxtStatus.Text   = "Running…";

        var sw = Stopwatch.StartNew();
        try
        {
            var results = await _client.SubmitAsync<dynamic>(query);
            sw.Stop();

            var labels = results
                .Select(r => (string)(r?.ToString() ?? string.Empty))
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            TxtResults.Text = string.Join(Environment.NewLine, labels);
            TxtStatus.Text  =
                $"{labels.Count} result(s)  ·  {sw.ElapsedMilliseconds} ms  ·  {_activeProfile?.Name ?? "unknown"}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            ShowError($"Query error ({sw.ElapsedMilliseconds} ms): {ex.Message}");
        }
        finally
        {
            BtnRun.IsEnabled = true;
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Clear buttons
    // ──────────────────────────────────────────────────────────────

    private void BtnClearResults_Click(object sender, RoutedEventArgs e)
    {
        TxtResults.Text = string.Empty;
        TxtStatus.Text  = "Ready";
    }

    private void BtnClearQuery_Click(object sender, RoutedEventArgs e)
        => TxtQuery.Text = string.Empty;

    // ──────────────────────────────────────────────────────────────
    // Query history
    // ──────────────────────────────────────────────────────────────

    private void CboHistory_SelectionChanged(object sender,
        SelectionChangedEventArgs e)
    {
        if (CboHistory.SelectedItem is HistoryEntry entry)
            TxtQuery.Text = entry.Query;
    }

    private void AddToHistory(string query)
    {
        var entry = new HistoryEntry(query);

        // Remove duplicate if present, then insert at top
        _queryHistory.RemoveAll(h => h.Query == query);
        _queryHistory.Insert(0, entry);

        if (_queryHistory.Count > MaxHistoryItems)
            _queryHistory.RemoveAt(_queryHistory.Count - 1);

        // Rebuild ComboBox items without triggering SelectionChanged
        CboHistory.SelectionChanged -= CboHistory_SelectionChanged;
        CboHistory.ItemsSource = null;
        CboHistory.ItemsSource = _queryHistory;
        CboHistory.SelectedIndex = 0;
        CboHistory.SelectionChanged += CboHistory_SelectionChanged;
    }

    // ──────────────────────────────────────────────────────────────
    // Relations tab
    // ──────────────────────────────────────────────────────────────

    private void BtnLoadVertices_Click(object sender, RoutedEventArgs e)
        => _ = LoadVerticesAsync();

    private void LstVertices_SelectionChanged(object sender,
        SelectionChangedEventArgs e)
    {
        LstEdges.ItemsSource        = null;
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
        if (_client is null)
        {
            TxtRelationsStatus.Text = "Not connected — click Connect first.";
            return;
        }

        BtnLoadVertices.IsEnabled = false;
        LstVertices.ItemsSource    = null;
        LstEdges.ItemsSource       = null;
        LstDestVertices.ItemsSource = null;
        TxtRelationsStatus.Text    = "Loading…";

        try
        {
            var results = await _client.SubmitAsync<dynamic>("g.V().label().dedup()");

            var vertices = results
                .Select(r => ParseLabel((JsonElement)(object)r))
                .OfType<VertexItem>()
                .OrderBy(v => v.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

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
        if (_client is null) return;

        TxtRelationsStatus.Text = $"Loading edge types for {vertex.Label}…";

        try
        {
            var outTask = _client.SubmitAsync<dynamic>(
                $"g.V().hasLabel({vertex.GremlinRef}).outE().label().dedup()");
            var inTask = _client.SubmitAsync<dynamic>(
                $"g.V().hasLabel({vertex.GremlinRef}).inE().label().dedup()");

            await Task.WhenAll(outTask, inTask);

            static IEnumerable<string> Labels(ResultSet<dynamic> rs) =>
                rs.Select(r => ParseLabel((JsonElement)(object)r)?.Label).OfType<string>();

            var edges = Labels(outTask.Result).Select(l => new EdgeLabelItem(l, IsOutgoing: true))
                .Concat(Labels(inTask.Result).Select(l => new EdgeLabelItem(l, IsOutgoing: false)))
                .OrderBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.IsOutgoing ? 0 : 1)   // out before in when same label
                .ToList();

            LstEdges.ItemsSource    = edges;
            TxtRelationsStatus.Text = $"{vertex.Label}  ·  {edges.Count} edge type(s)";
        }
        catch (Exception ex)
        {
            TxtRelationsStatus.Text = $"Error: {ex.Message}";
        }
    }

    private void LstEdges_SelectionChanged(object sender,
        SelectionChangedEventArgs e)
    {
        LstDestVertices.ItemsSource = null;
        if (LstVertices.SelectedItem is VertexItem srcVertex &&
            LstEdges.SelectedItem is EdgeLabelItem edgeLabel)
            _ = LoadDestVerticesAsync(srcVertex, edgeLabel);

        RenderRelationsGraph();
    }

    private void LstDestVertices_SelectionChanged(object sender,
        SelectionChangedEventArgs e)
        => RenderRelationsGraph();

    private async Task LoadDestVerticesAsync(VertexItem srcVertex, EdgeLabelItem edgeLabel)
    {
        if (_client is null) return;

        var arrow = edgeLabel.IsOutgoing ? "→" : "←";
        TxtRelationsStatus.Text = $"Loading connections for {srcVertex.Label} {arrow} {edgeLabel.Label}…";

        try
        {
            // Outgoing: follow the edge forward; incoming: follow it backward
            var query = edgeLabel.IsOutgoing
                ? $"g.V().hasLabel({srcVertex.GremlinRef}).outE().hasLabel({edgeLabel.GremlinRef}).inV().label().dedup()"
                : $"g.V().hasLabel({srcVertex.GremlinRef}).inE().hasLabel({edgeLabel.GremlinRef}).outV().label().dedup()";

            var results = await _client.SubmitAsync<dynamic>(query);

            var destVertices = results
                .Select(r => ParseLabel((JsonElement)(object)r))
                .OfType<VertexItem>()
                .OrderBy(v => v.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            LstDestVertices.ItemsSource = destVertices;
            TxtRelationsStatus.Text     =
                $"{srcVertex.Label}  {arrow}  {edgeLabel.Label}  ·  {destVertices.Count} vertex type(s)";
        }
        catch (Exception ex)
        {
            TxtRelationsStatus.Text = $"Error: {ex.Message}";
        }
    }

    // ── Relations mini-graph ──────────────────────────────────────

    private async Task LoadRelationsEdgesAsync(VertexItem vertex)
    {
        if (_client is null) return;

        // Single query: all schema edges where this vertex type is either endpoint
        var query =
            $"g.E().where(outV().hasLabel({vertex.GremlinRef}).or().inV().hasLabel({vertex.GremlinRef}))" +
            ".project('f','l','t').by(outV().label()).by(label()).by(inV().label()).dedup()";
        try
        {
            var results = await _client.SubmitAsync<dynamic>(query);
            foreach (var r in results)
            {
                var edge = ParseSchemaEdge((JsonElement)(object)r);
                if (edge is not null) _relationsGraphEdges.Add(edge);
            }
        }
        catch { /* best-effort — preview panel is non-critical */ }

        RenderRelationsGraph();
    }

    private void RenderRelationsGraph()
    {
        RelationsCanvas.Children.Clear();

        if (LstVertices.SelectedItem is not VertexItem srcVertex) return;

        var selectedEdge = LstEdges.SelectedItem      as EdgeLabelItem;
        var selectedDest = LstDestVertices.SelectedItem as VertexItem;

        double w = RelationsCanvas.ActualWidth  > 50 ? RelationsCanvas.ActualWidth  : 380;
        double h = RelationsCanvas.ActualHeight > 50 ? RelationsCanvas.ActualHeight : 400;

        // ── Filter edges based on current selection depth ──────────
        IEnumerable<GraphSchemaEdge> edges = _relationsGraphEdges;

        if (selectedEdge is not null)
        {
            edges = selectedEdge.IsOutgoing
                ? edges.Where(e => e.From == srcVertex.Label && e.EdgeLabel == selectedEdge.Label)
                : edges.Where(e => e.To   == srcVertex.Label && e.EdgeLabel == selectedEdge.Label);
        }

        if (selectedDest is not null && selectedEdge is not null)
        {
            edges = selectedEdge.IsOutgoing
                ? edges.Where(e => e.To   == selectedDest.Label)
                : edges.Where(e => e.From == selectedDest.Label);
        }

        var filtered = edges.ToList();

        // ── Side-node collections ──────────────────────────────────
        // outNodes: vertex types that receive an edge FROM srcVertex (rendered right)
        var outNodes = filtered
            .Where(e => e.From == srcVertex.Label && e.To != srcVertex.Label)
            .Select(e => e.To).Distinct().ToList();

        // inNodes: vertex types that send an edge TO srcVertex (rendered left)
        var inNodes = filtered
            .Where(e => e.To == srcVertex.Label && e.From != srcVertex.Label)
            .Select(e => e.From).Distinct().ToList();

        // ── Stable colour map (sorted so assignment never changes) ─
        var edgeColors = _relationsGraphEdges
            .Select(e => e.EdgeLabel).Distinct()
            .OrderBy(l => l, StringComparer.Ordinal)
            .Select((label, i) => (label,
                brush: new SolidColorBrush(_edgePalette[i % _edgePalette.Length])))
            .ToDictionary(x => x.label, x => x.brush);

        // ── Layout ────────────────────────────────────────────────
        double cx = w / 2, cy = h / 2;
        double rightX = w * 0.80, leftX = w * 0.20;

        double Step(int count) => count <= 1
            ? 0
            : Math.Min(NodeH + 18, (h - NodeH * 2.5) / (count - 1));

        var outPos = outNodes
            .Select((label, i) => (label, x: rightX,
                y: cy + (i - (outNodes.Count - 1) / 2.0) * Step(outNodes.Count)))
            .ToList();

        var inPos = inNodes
            .Select((label, i) => (label, x: leftX,
                y: cy + (i - (inNodes.Count - 1) / 2.0) * Step(inNodes.Count)))
            .ToList();

        var outPosMap = outPos.ToDictionary(p => p.label, p => (p.x, p.y));
        var inPosMap  = inPos .ToDictionary(p => p.label, p => (p.x, p.y));

        // ── Draw edges ────────────────────────────────────────────
        // Group by (from,to) so parallel edges get curvature offsets
        foreach (var grp in filtered.Where(e => e.From != e.To).GroupBy(e => (e.From, e.To)))
        {
            bool isOut = grp.Key.From == srcVertex.Label;
            var labels = grp.Select(e => e.EdgeLabel).ToList();

            for (int i = 0; i < labels.Count; i++)
            {
                var brush = edgeColors.GetValueOrDefault(labels[i],
                                new SolidColorBrush(_edgePalette[0]));
                double curv = (i - (labels.Count - 1) / 2.0) * 40;

                if (isOut && outPosMap.TryGetValue(grp.Key.To, out var tp))
                    DrawEdge(RelationsCanvas, cx, cy, tp.x, tp.y, labels[i], curv, brush);
                else if (!isOut && inPosMap.TryGetValue(grp.Key.From, out var fp))
                    DrawEdge(RelationsCanvas, fp.x, fp.y, cx, cy, labels[i], curv, brush);
            }
        }

        // Self-loops on the source vertex
        var selfEdges = filtered.Where(e => e.From == e.To).ToList();
        for (int i = 0; i < selfEdges.Count; i++)
        {
            var brush = edgeColors.GetValueOrDefault(selfEdges[i].EdgeLabel,
                            new SolidColorBrush(_edgePalette[0]));
            DrawSelfLoop(RelationsCanvas, cx, cy, selfEdges[i].EdgeLabel, i, brush);
        }

        // ── Draw nodes (side first, centre on top) ────────────────
        foreach (var (label, x, y) in outPos)
            DrawNodeOnCanvas(RelationsCanvas, x, y, label,
                isSelected: label == selectedDest?.Label && selectedEdge?.IsOutgoing == true);

        foreach (var (label, x, y) in inPos)
            DrawNodeOnCanvas(RelationsCanvas, x, y, label,
                isSelected: label == selectedDest?.Label && selectedEdge?.IsOutgoing == false);

        DrawNodeOnCanvas(RelationsCanvas, cx, cy, srcVertex.Label, isSelected: true);
    }

    // ── Relations preview — pan / zoom / toggle-selection ─────────

    private void RelationsCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed && e.MiddleButton != MouseButtonState.Pressed)
            return;
        _isRelationsPanning = true;
        _relationsPanStart  = e.GetPosition(RelationsPreviewBorder);
        RelationsCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void RelationsCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isRelationsPanning) return;
        if (e.LeftButton != MouseButtonState.Pressed && e.MiddleButton != MouseButtonState.Pressed)
            return;
        var current = e.GetPosition(RelationsPreviewBorder);
        var delta   = current - _relationsPanStart;
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
        var pos   = e.GetPosition(RelationsPreviewBorder);
        var m     = _relationsTransform.Matrix;
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
        => ToggleListBoxSelection(LstVertices, e);

    private void LstEdges_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        => ToggleListBoxSelection(LstEdges, e);

    private void LstDestVertices_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        => ToggleListBoxSelection(LstDestVertices, e);

    // ── Parsers ────────────────────────────────────────────────────

    // All label queries (g.V().label().dedup(), outE().label().dedup(), inV().label().dedup())
    // return plain JSON strings — wrap them in a VertexItem.
    private static VertexItem? ParseLabel(JsonElement el) =>
        el.ValueKind == JsonValueKind.String ? new VertexItem(el.GetString()!) : null;

    // ──────────────────────────────────────────────────────────────
    // Graph tab
    // ──────────────────────────────────────────────────────────────

    // Mutable node: position updated by force layout and dragging
    private sealed class GraphNode(string label)
    {
        public string Label { get; } = label;
        public double X  { get; set; }
        public double Y  { get; set; }
        public double Fx { get; set; }   // accumulated force
        public double Fy { get; set; }
    }

    private sealed record GraphSchemaEdge(string From, string EdgeLabel, string To);

    private readonly List<GraphNode>       _graphNodes = [];
    private readonly List<GraphSchemaEdge> _graphEdges = [];
    private readonly List<GraphSchemaEdge> _relationsGraphEdges = [];
    private GraphNode? _dragNode;
    private Point      _dragOffset;

    // Graph tab — pan / zoom state
    private readonly MatrixTransform _canvasTransform = new();
    private bool  _isPanning;
    private Point _panStart;

    // Relations preview — pan / zoom state
    private readonly MatrixTransform _relationsTransform = new();
    private bool  _isRelationsPanning;
    private Point _relationsPanStart;

    // Static brushes — created once, reused across renders
    private static readonly SolidColorBrush _edgeLabelBg = new(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly SolidColorBrush _nodeBorder  = new(Color.FromRgb(0x4E, 0xC9, 0xB0));
    private static readonly SolidColorBrush _nodeBg      = new(Color.FromRgb(0x2D, 0x2D, 0x30));
    private static readonly SolidColorBrush _nodeFg      = new(Color.FromRgb(0x4E, 0xC9, 0xB0));

    // Palette cycled per unique edge label so each relationship type has a distinct colour
    private static readonly Color[] _edgePalette =
    [
        Color.FromRgb(0x56, 0x9C, 0xD6),  // blue
        Color.FromRgb(0x4E, 0xC9, 0xB0),  // teal
        Color.FromRgb(0xDC, 0xDC, 0xAA),  // yellow
        Color.FromRgb(0xC5, 0x86, 0xC0),  // purple
        Color.FromRgb(0xCE, 0x91, 0x78),  // orange
        Color.FromRgb(0x57, 0xA6, 0x4A),  // green
        Color.FromRgb(0xF4, 0x4B, 0x4B),  // red
        Color.FromRgb(0x9C, 0xDC, 0xFE),  // light blue
    ];

    private void BtnLoadGraph_Click(object sender, RoutedEventArgs e)
        => _ = LoadGraphAsync();

    private void BtnResetView_Click(object sender, RoutedEventArgs e)
        => _canvasTransform.Matrix = Matrix.Identity;

    private async Task LoadGraphAsync()
    {
        if (_client is null) { TxtGraphStatus.Text = "Not connected — click Connect first."; return; }

        BtnLoadGraph.IsEnabled = false;
        TxtGraphStatus.Text    = "Loading schema…";
        GraphCanvas.Children.Clear();
        _graphNodes.Clear();
        _graphEdges.Clear();
        _canvasTransform.Matrix = Matrix.Identity;

        try
        {
            // One query returns every unique (fromLabel, edgeLabel, toLabel) triple
            var results = await _client.SubmitAsync<dynamic>(
                "g.E().project('f','l','t').by(outV().label()).by(label()).by(inV().label()).dedup()");

            var nodeSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in results)
            {
                var edge = ParseSchemaEdge((JsonElement)(object)r);
                if (edge is null) continue;
                _graphEdges.Add(edge);
                nodeSet.Add(edge.From);
                nodeSet.Add(edge.To);
            }

            foreach (var lbl in nodeSet.OrderBy(s => s))
                _graphNodes.Add(new GraphNode(lbl));

            PlaceNodesCircular();
            RunForceLayout(250);
            RenderGraph();

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

    // ── Layout ────────────────────────────────────────────────────

    private void PlaceNodesCircular()
    {
        var w = GraphCanvas.ActualWidth  > 50 ? GraphCanvas.ActualWidth  : 900;
        var h = GraphCanvas.ActualHeight > 50 ? GraphCanvas.ActualHeight : 600;
        int n = _graphNodes.Count;
        // Grow the radius so nodes aren't already overlapping before the simulation starts
        var r = Math.Min(w, h) * Math.Clamp(0.30 + n * 0.012, 0.30, 0.44);
        for (int i = 0; i < n; i++)
        {
            var a = 2 * Math.PI * i / Math.Max(n, 1) - Math.PI / 2;
            _graphNodes[i].X = w / 2 + r * Math.Cos(a);
            _graphNodes[i].Y = h / 2 + r * Math.Sin(a);
        }
    }

    private void RunForceLayout(int iterations)
    {
        if (_graphNodes.Count == 0) return;

        var w = GraphCanvas.ActualWidth  > 50 ? GraphCanvas.ActualWidth  : 900;
        var h = GraphCanvas.ActualHeight > 50 ? GraphCanvas.ActualHeight : 600;
        int n = _graphNodes.Count;

        // Adaptive parameters
        double repulsion = Math.Max(6000, 1800.0 * n);
        double springK   = 0.04;
        double restLen   = Math.Clamp(160 + n * 10, 150, 340.0);
        double gravity   = 0.018;   // gentle pull toward canvas centre
        double decay     = 0.87;    // velocity damping per step

        // Velocity arrays (not stored on nodes to keep GraphNode lean)
        var vx = new double[n];
        var vy = new double[n];

        // Deduplicate spring pairs so duplicate edge labels don't add extra pull
        var springPairs = _graphEdges
            .Where(e => e.From != e.To)
            .Select(e => (e.From, e.To))
            .Distinct()
            .Select(p => (_graphNodes.FindIndex(x => x.Label == p.From),
                          _graphNodes.FindIndex(x => x.Label == p.To)))
            .Where(p => p.Item1 >= 0 && p.Item2 >= 0)
            .ToList();

        for (int iter = 0; iter < iterations; iter++)
        {
            // Cooling factor: starts at 1, approaches 0 — forces shrink as layout stabilises
            double alpha = Math.Max(0.005, 1.0 - (double)iter / iterations);

            for (int i = 0; i < n; i++) { _graphNodes[i].Fx = 0; _graphNodes[i].Fy = 0; }

            // Repulsion — every pair
            for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                var a = _graphNodes[i]; var b = _graphNodes[j];
                double dx = a.X - b.X, dy = a.Y - b.Y;
                double d2 = dx * dx + dy * dy + 1;
                double d  = Math.Sqrt(d2);
                double f  = repulsion / d2;
                a.Fx += f * dx / d; a.Fy += f * dy / d;
                b.Fx -= f * dx / d; b.Fy -= f * dy / d;
            }

            // Spring attraction — one spring per unique node pair
            foreach (var (ai, bi) in springPairs)
            {
                var a = _graphNodes[ai]; var b = _graphNodes[bi];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double d  = Math.Sqrt(dx * dx + dy * dy) + 0.01;
                double f  = springK * (d - restLen);
                a.Fx += f * dx / d; a.Fy += f * dy / d;
                b.Fx -= f * dx / d; b.Fy -= f * dy / d;
            }

            // Gravity toward canvas centre — keeps disconnected subgraphs from drifting away
            for (int i = 0; i < n; i++)
            {
                _graphNodes[i].Fx += gravity * (w / 2 - _graphNodes[i].X);
                _graphNodes[i].Fy += gravity * (h / 2 - _graphNodes[i].Y);
            }

            // Velocity integration with cooling
            for (int i = 0; i < n; i++)
            {
                vx[i] = (vx[i] + _graphNodes[i].Fx * alpha) * decay;
                vy[i] = (vy[i] + _graphNodes[i].Fy * alpha) * decay;
                _graphNodes[i].X += vx[i];
                _graphNodes[i].Y += vy[i];
            }
        }

        // Final re-centre
        double cx = _graphNodes.Average(node => node.X);
        double cy = _graphNodes.Average(node => node.Y);
        double ox = w / 2 - cx, oy = h / 2 - cy;
        foreach (var node in _graphNodes) { node.X += ox; node.Y += oy; }
    }

    // ── Rendering ─────────────────────────────────────────────────

    private const double NodeW = 130;
    private const double NodeH = 36;

    private void RenderGraph()
    {
        GraphCanvas.Children.Clear();

        // Assign a stable colour to each unique edge label (sorted → deterministic)
        var edgeColors = _graphEdges
            .Select(e => e.EdgeLabel)
            .Distinct()
            .OrderBy(l => l, StringComparer.Ordinal)
            .Select((label, i) => (label, brush: new SolidColorBrush(_edgePalette[i % _edgePalette.Length])))
            .ToDictionary(x => x.label, x => x.brush);

        var map = _graphNodes.ToDictionary(n => n.Label);

        // Group by (from,to) so multiple edges between the same pair get distinct curvature offsets
        foreach (var grp in _graphEdges.GroupBy(e => (e.From, e.To)))
        {
            if (!map.TryGetValue(grp.Key.From, out var from)) continue;
            if (!map.TryGetValue(grp.Key.To,   out var to))   continue;

            var labels = grp.Select(e => e.EdgeLabel).ToList();
            bool isSelf = grp.Key.From == grp.Key.To;

            for (int i = 0; i < labels.Count; i++)
            {
                var brush = edgeColors.GetValueOrDefault(labels[i],
                                new SolidColorBrush(_edgePalette[0]));
                if (isSelf)
                    DrawSelfLoop(GraphCanvas, from.X, from.Y, labels[i], i, brush);
                else
                    DrawEdge(GraphCanvas, from.X, from.Y, to.X, to.Y, labels[i],
                             curvature: (i - (labels.Count - 1) / 2.0) * 50,
                             edgeBrush: brush);
            }
        }

        // Draw nodes on top so they cover edge endpoints
        foreach (var node in _graphNodes)
        {
            var border = new Border
            {
                Width           = NodeW,
                Height          = NodeH,
                Background      = _nodeBg,
                BorderBrush     = _nodeBorder,
                BorderThickness = new Thickness(1.5),
                CornerRadius    = new CornerRadius(18),
                Cursor          = Cursors.SizeAll,
                Child           = new TextBlock
                {
                    Text                = node.Label,
                    Foreground          = _nodeFg,
                    FontSize            = 12,
                    FontWeight          = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center
                }
            };

            Canvas.SetLeft(border, node.X - NodeW / 2);
            Canvas.SetTop(border,  node.Y - NodeH / 2);
            GraphCanvas.Children.Add(border);
        }
    }

    private void DrawEdge(Canvas canvas, double x1, double y1, double x2, double y2,
                          string label, double curvature, SolidColorBrush edgeBrush)
    {
        var dx   = x2 - x1;  var dy   = y2 - y1;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < 1) return;

        var ux = dx / dist;  var uy = dy / dist;  // unit along edge
        var px = -uy;        var py = ux;          // unit perpendicular

        const double margin = NodeH / 2 + 2;
        var sx  = x1 + ux * margin;  var sy  = y1 + uy * margin;  // start
        var ex  = x2 - ux * margin;  var ey  = y2 - uy * margin;  // end
        var cpx = (sx + ex) / 2 + px * curvature;                  // bezier control point
        var cpy = (sy + ey) / 2 + py * curvature;

        // Curved line
        var fig = new PathFigure { StartPoint = new Point(sx, sy), IsClosed = false };
        fig.Segments.Add(new QuadraticBezierSegment(new Point(cpx, cpy), new Point(ex, ey), true));
        canvas.Children.Add(new WpfPath
        {
            Data            = new PathGeometry { Figures = { fig } },
            Stroke          = edgeBrush,
            StrokeThickness = 1.5
        });

        // Arrowhead: tangent at end of quadratic bezier = direction (end - control)
        var tx = ex - cpx;  var ty = ey - cpy;
        var tl = Math.Sqrt(tx * tx + ty * ty);
        if (tl > 0.01)
        {
            tx /= tl; ty /= tl;
            const double a = 9.0, w = 3.8;
            canvas.Children.Add(new Polygon
            {
                Points          = new PointCollection {
                    new(ex, ey),
                    new(ex - a * tx + w * (-ty), ey - a * ty + w * tx),
                    new(ex - a * tx - w * (-ty), ey - a * ty - w * tx)
                },
                Fill            = edgeBrush,
                StrokeThickness = 0
            });
        }

        // Label at bezier midpoint (t=0.5)
        var lx = 0.25 * sx + 0.5 * cpx + 0.25 * ex;
        var ly = 0.25 * sy + 0.5 * cpy + 0.25 * ey;
        var txt = new TextBlock
        {
            Text       = label,
            Foreground = edgeBrush,
            Background = _edgeLabelBg,
            FontSize   = 10,
            Padding    = new Thickness(2, 0, 2, 0)
        };
        Canvas.SetLeft(txt, lx - 28);
        Canvas.SetTop(txt,  ly - 8);
        canvas.Children.Add(txt);
    }

    private void DrawSelfLoop(Canvas canvas, double nodeCx, double nodeCy,
                              string label, int index, SolidColorBrush edgeBrush)
    {
        double ox = (index - 0.5) * 28;
        var cx = nodeCx + ox;
        var cy = nodeCy - NodeH / 2;

        var fig = new PathFigure { StartPoint = new Point(cx - 16, cy), IsClosed = false };
        fig.Segments.Add(new BezierSegment(
            new Point(cx - 42, cy - 55),
            new Point(cx + 42, cy - 55),
            new Point(cx + 16, cy), true));
        canvas.Children.Add(new WpfPath
        {
            Data            = new PathGeometry { Figures = { fig } },
            Stroke          = edgeBrush,
            StrokeThickness = 1.5
        });

        var txt = new TextBlock
        {
            Text       = label,
            Foreground = edgeBrush,
            Background = _edgeLabelBg,
            FontSize   = 10,
            Padding    = new Thickness(2, 0, 2, 0)
        };
        Canvas.SetLeft(txt, cx - 28);
        Canvas.SetTop(txt,  cy - 68 - index * 16);
        canvas.Children.Add(txt);
    }

    private static void DrawNodeOnCanvas(Canvas canvas, double x, double y, string label,
                                         bool isSelected = false)
    {
        var border = new Border
        {
            Width           = NodeW,
            Height          = NodeH,
            Background      = _nodeBg,
            BorderBrush     = isSelected
                                  ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00))
                                  : _nodeBorder,
            BorderThickness = new Thickness(isSelected ? 2.5 : 1.5),
            CornerRadius    = new CornerRadius(18),
            Child           = new TextBlock
            {
                Text                = label,
                Foreground          = _nodeFg,
                FontSize            = 12,
                FontWeight          = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            }
        };
        Canvas.SetLeft(border, x - NodeW / 2);
        Canvas.SetTop(border,  y - NodeH / 2);
        canvas.Children.Add(border);
    }

    // ── Drag / pan / zoom handling ────────────────────────────────

    private void GraphCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.MiddleButton == MouseButtonState.Pressed)
        {
            _isPanning = true;
            _panStart  = e.GetPosition(GraphBorder);
            GraphCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(GraphCanvas);
        _dragNode = _graphNodes.FirstOrDefault(n =>
            Math.Abs(n.X - pos.X) < NodeW / 2 + 2 &&
            Math.Abs(n.Y - pos.Y) < NodeH / 2 + 2);

        if (_dragNode is not null)
        {
            _dragOffset = new Point(pos.X - _dragNode.X, pos.Y - _dragNode.Y);
            GraphCanvas.CaptureMouse();
        }
        else
        {
            // Left-click on empty canvas → pan
            _isPanning = true;
            _panStart  = e.GetPosition(GraphBorder);
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
            RenderGraph();
            return;
        }

        if (_isPanning && (e.LeftButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed))
        {
            var current = e.GetPosition(GraphBorder);
            var delta   = current - _panStart;
            _panStart   = current;
            var m = _canvasTransform.Matrix;
            m.Translate(delta.X, delta.Y);
            _canvasTransform.Matrix = m;
        }
    }

    private void GraphCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragNode  = null;
        _isPanning = false;
        GraphCanvas.ReleaseMouseCapture();
    }

    private void GraphCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        const double factor = 1.12;
        var scale = e.Delta > 0 ? factor : 1.0 / factor;
        var pos   = e.GetPosition(GraphBorder);
        var m     = _canvasTransform.Matrix;
        m.ScaleAt(scale, scale, pos.X, pos.Y);
        _canvasTransform.Matrix = m;
        e.Handled = true;
    }

    // ── Parser ────────────────────────────────────────────────────

    private static GraphSchemaEdge? ParseSchemaEdge(JsonElement el)
    {
        string? f = null, l = null, t = null;

        // GraphSON2 g:Map: {"@type":"g:Map","@value":["f","..","l","..","t",".."]}
        if (el.TryGetProperty("@type", out var type) && type.GetString() == "g:Map" &&
            el.TryGetProperty("@value", out var arr)  && arr.ValueKind == JsonValueKind.Array)
        {
            var items = arr.EnumerateArray().ToList();
            for (int i = 0; i + 1 < items.Count; i += 2)
            {
                var k = items[i].ValueKind   == JsonValueKind.String ? items[i].GetString()   : null;
                var v = items[i+1].ValueKind == JsonValueKind.String ? items[i+1].GetString() : null;
                if (k == "f") f = v; else if (k == "l") l = v; else if (k == "t") t = v;
            }
        }
        else if (el.ValueKind == JsonValueKind.Object)  // plain JSON object fallback
        {
            if (el.TryGetProperty("f", out var fv)) f = fv.GetString();
            if (el.TryGetProperty("l", out var lv)) l = lv.GetString();
            if (el.TryGetProperty("t", out var tv)) t = tv.GetString();
        }

        return f is not null && l is not null && t is not null
            ? new GraphSchemaEdge(f, l, t) : null;
    }

    // ──────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────

    private void ShowError(string message)
    {
        TxtResults.Text = $"ERROR\n\n{message}";
        TxtStatus.Text  = "Error";
    }

    private enum ConnectionStatus { Disconnected, Connecting, Connected, Error }

    private void SetStatus(ConnectionStatus status)
    {
        (StatusIndicator.Fill, StatusIndicator.ToolTip) = status switch
        {
            ConnectionStatus.Connected    => (new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10)), "Connected"),
            ConnectionStatus.Connecting   => (new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x00)), "Connecting…"),
            ConnectionStatus.Error        => (new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38)), "Connection error"),
            _                             => (new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), "Not connected"),
        };
    }

    private void DisposeClient()
    {
        _client?.Dispose();
        _client        = null;
        _activeProfile = null;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
        => DisposeClient();
}
