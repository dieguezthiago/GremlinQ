# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build GremlinQ.slnx

# Test
dotnet test GremlinQ.slnx
dotnet test Tests/GremlinQ.Core.Tests/GremlinQ.Core.Tests.csproj
dotnet test Tests/GremlinQ.Infrastructure.Tests/GremlinQ.Infrastructure.Tests.csproj

# Run a single test
dotnet test --filter "FullyQualifiedName~TestMethodName"
```

## Architecture

GremlinQ is a WPF desktop app (net10.0-windows) for exploring Azure Cosmos DB graph databases via Gremlin queries. It visualizes graph schema with a force-directed layout.

### Project layout

```
Src/
  GremlinQ.App/       — WPF frontend + all current business logic
  GremlinQ.Core/      — empty; intended for domain abstractions
  GremlinQ.Infrastructure/ — empty; depends on Core; intended for Gremlin client wrappers
Tests/
  GremlinQ.Core.Tests/
  GremlinQ.Infrastructure.Tests/
```

`GremlinQ.Core` and `GremlinQ.Infrastructure` are placeholders — the current logic lives entirely in `GremlinQ.App`. Future work should migrate services and models there.

### Layers inside GremlinQ.App

- **Services** — all business logic behind interfaces (`IGremlinConnectionService`, `IGremlinQueryService`, `IGraphSchemaService`, `IConnectionProfileRepository`, `IQueryHistoryManager`). All registered as singletons in `App.xaml.cs`.
- **Models** — plain records/classes: `GraphNode`, `GraphSchema`, `GraphSchemaEdge`, `VertexItem`, `EdgeLabelItem`, `QueryResult`, `HistoryEntry`.
- **Rendering** — `GraphCanvasRenderer` (full schema graph), `RelationsCanvasRenderer` (focused vertex relations), `CanvasDrawingHelper` (WPF primitives). Edge colors are deterministic: labels sorted alphabetically, cycled through 8 fixed colors.
- **Layout** — `ForceDirectedLayoutEngine`: circular init → 250 iterations of repulsion/spring/gravity with cooling; parameters scale with node count.
- **MainWindow** — single large code-behind that coordinates all UI state; intentionally not MVVM for this PoC.

### Key technical details

**Azure Cosmos DB GraphSON2 quirk** — `CosmosDbGraphSON2Reader` is a custom `IMessageSerializer` that tolerates bare JSON numbers as vertex IDs, which the standard Gremlin.Net serializer rejects.

**Gremlin query safety** — `VertexItem.GremlinRef` and `EdgeLabelItem.GremlinRef` escape single quotes before interpolating labels into Gremlin traversals. Always use these properties, not `Label` directly.

**Connection profiles** — loaded from `connections/*.json` at startup. Sorted by priority: emulator → dev → staging → prod → other. Keys are stored in the JSON files; never commit real keys.

**Canvas interactions** — left-drag moves nodes or pans, middle-drag pans, scroll wheel zooms (factor 1.12). Node hit detection uses a 130×36 rectangle.
