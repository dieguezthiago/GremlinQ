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
  GremlinQ.Core/             — domain models + service interfaces (no dependencies)
    Models/                  — ConnectionProfile, GraphSchema, GraphSchemaEdge, VertexItem,
                               EdgeLabelItem, QueryResult, HistoryEntry
    Services/                — IGremlinConnectionService, IGremlinQueryService,
                               IGraphSchemaService, IConnectionProfileRepository, IQueryHistoryManager
  GremlinQ.Infrastructure/   — service implementations + Gremlin client wrappers (depends on Core)
    Services/                — GremlinConnectionService, GremlinQueryService, GraphSchemaService,
                               ConnectionProfileRepository
    CosmosDbMessageSerializer.cs
  GremlinQ.App/              — WPF frontend (depends on Core + Infrastructure)
    Models/GraphNode.cs      — mutable layout node (X/Y/Fx/Fy), UI-only
    Services/QueryHistoryManager.cs — pure in-memory app state
    Rendering/               — GraphCanvasRenderer, RelationsCanvasRenderer, CanvasDrawingHelper
    Layout/                  — ForceDirectedLayoutEngine
Tests/
  GremlinQ.Core.Tests/
  GremlinQ.Infrastructure.Tests/
```

Dependency rule: App → Infrastructure → Core (nothing points back up).

### Layer responsibilities

- **Core** — domain value types and service contracts. No external dependencies.
- **Infrastructure** — Gremlin.Net client, Cosmos DB serialization, JSON profile loading. Owns the `Gremlin.Net` NuGet reference.
- **App** — WPF UI, DI composition root (`App.xaml.cs`), rendering, force-directed layout. All services registered as singletons.

### Key technical details

**Azure Cosmos DB GraphSON2 quirk** — `CosmosDbMessageSerializer` is a custom `IMessageSerializer` that tolerates bare JSON numbers as vertex IDs, which the standard Gremlin.Net serializer rejects.

**GraphSchema.Nodes type** — `IReadOnlyList<VertexItem>` (domain type). `MainWindow.LoadGraphAsync()` maps these to `GraphNode` instances for the layout engine.

**Gremlin query safety** — `VertexItem.GremlinRef` and `EdgeLabelItem.GremlinRef` escape single quotes before interpolating labels into Gremlin traversals. Always use these properties, not `Label` directly.

**Connection profiles** — loaded from `connections/*.json` at startup. Sorted by priority: emulator → dev → staging → prod → other. Keys are stored in the JSON files; never commit real keys.

**Canvas interactions** — left-drag moves nodes or pans, middle-drag pans, scroll wheel zooms (factor 1.12). Node hit detection uses a 130×36 rectangle.
