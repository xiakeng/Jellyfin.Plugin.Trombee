# Entry Points and Code Flows

This guide starts from every way Jellyfin or the web client enters Trombee and follows the call path to its side effects.

## 1. Plugin Construction and Embedded Pages

Jellyfin constructs `Plugin`, which stores the active plugin instance and exposes two embedded pages.

```mermaid
flowchart LR
    Host[Jellyfin plugin loader] --> Constructor[Plugin constructor]
    Constructor --> Instance[Plugin.Instance]
    Host --> GetPages[Plugin.GetPages]
    GetPages --> Settings[configPage.html]
    GetPages --> Browse[actorsBrowse.html]
```

Source: [`Plugin.cs`](../Jellyfin.Plugin.ActorsIndex/Plugin.cs).

## 2. Dependency Registration

`PluginServiceRegistrator` defines the process-wide service graph.

```mermaid
flowchart TD
    Host[Jellyfin application host] --> Register[RegisterServices]
    Register --> Store[Singleton SQLite store]
    Register --> Source[Singleton index source]
    Register --> Maintenance[Singleton maintenance service]
    Register --> Query[Singleton read service]
    Register --> Tasks[Three scheduled tasks]
    Register --> Hosted[Initialization, Plugin Pages, live monitor]
    Store --> Path[IApplicationPaths.DataPath/trombee/actors-index.db]
```

Source: [`PluginServiceRegistrator.cs`](../Jellyfin.Plugin.ActorsIndex/PluginServiceRegistrator.cs).

## 3. Database Startup and Schema Change

The hosted initialization service calls the idempotent store initializer. Schema resources are sorted, normalized, and hashed as one set.

```mermaid
flowchart TD
    Start[Jellyfin starts hosted services] --> Init[ActorsIndexInitializationService.StartAsync]
    Init --> Initialize[SqliteActorsIndexStore.InitializeAsync]
    Initialize --> Exists{Database exists?}
    Exists -- No --> Create[Create temporary schema database]
    Exists -- Yes --> Hash{Stored hash equals embedded hash?}
    Hash -- Yes --> Unchanged[Return Unchanged]
    Hash -- No --> Recreate[Create new schema database and replace old file]
    Create --> Empty[No active generation]
    Recreate --> Empty
    Init --> Log[Log result or initialization failure]
```

The hidden bootstrap task handles the empty active-generation state after scheduled tasks are registered.

Sources: [`ActorsIndexInitializationService.cs`](../Jellyfin.Plugin.ActorsIndex/Services/ActorsIndexInitializationService.cs), [`EmbeddedSqlResourceProvider.cs`](../Jellyfin.Plugin.ActorsIndex/Persistence/Sql/EmbeddedSqlResourceProvider.cs), and [`SqliteActorsIndexStore.cs`](../Jellyfin.Plugin.ActorsIndex/Persistence/SqliteActorsIndexStore.cs).

## 4. Hidden Startup Bootstrap

```mermaid
sequenceDiagram
    participant Jellyfin as Jellyfin task manager
    participant Bootstrap as ActorsIndexBootstrapTask
    participant Store as SQLite store
    participant Rebuild as RebuildActorsIndexTask

    Jellyfin->>Bootstrap: Startup trigger
    Bootstrap->>Store: HasActiveGenerationAsync
    alt no active generation
        Bootstrap->>Jellyfin: QueueIfNotRunning<RebuildActorsIndexTask>
        Jellyfin->>Rebuild: ExecuteAsync
    else active generation exists
        Bootstrap-->>Jellyfin: Complete without queueing
    end
```

The task is hidden, enabled, logged, and triggered only at startup. Source: [`ActorsIndexBootstrapTask.cs`](../Jellyfin.Plugin.ActorsIndex/Tasks/ActorsIndexBootstrapTask.cs).

## 5. Manual Full Rebuild

The visible manual task has no default trigger.

```mermaid
sequenceDiagram
    participant Admin as Jellyfin administrator
    participant Task as RebuildActorsIndexTask
    participant Service as ActorsIndexMaintenanceService
    participant Source as JellyfinActorsIndexSource
    participant Store as SQLite store

    Admin->>Task: Run scheduled task
    Task->>Service: FullRebuildAsync
    Service->>Store: BeginRebuildAsync
    Store-->>Service: building generation ID
    loop batches of 200 source items
        Service->>Source: GetItemsAsync(null)
        Service->>Store: ReplaceItemsAsync(building generation)
        Service-->>Task: report progress
    end
    Service->>Store: ActivateRebuildAsync
    Store->>Store: mark complete, switch active generation, delete old generations
```

If enumeration, cancellation, or activation fails, the existing active generation is never replaced. Sources: [`RebuildActorsIndexTask.cs`](../Jellyfin.Plugin.ActorsIndex/Tasks/RebuildActorsIndexTask.cs) and [`ActorsIndexMaintenanceService.cs`](../Jellyfin.Plugin.ActorsIndex/Services/ActorsIndexMaintenanceService.cs).

## 6. Daily Incremental Maintenance

`UpdateActorsIndexTask` defaults to 03:00 daily.

```mermaid
flowchart TD
    Trigger[Daily 03:00 or manual run] --> Incremental[IncrementalUpdateAsync]
    Incremental --> Active{Active generation exists?}
    Active -- No --> Full[Run full rebuild]
    Active -- Yes --> Watermark[Read prior watermark]
    Watermark --> Overlap[Subtract five minutes]
    Overlap --> Modified[Enumerate items modified since overlap]
    Modified --> Upsert[Upsert batches into active generation]
    Upsert --> Current[Read current Jellyfin source IDs]
    Current --> Deleted[Delete stored IDs no longer present]
    Deleted --> Complete[Advance success time and watermark]
```

The overlap protects against timestamp precision and events near the previous run boundary. The watermark advances only after upserts and deletion reconciliation succeed. Sources: [`UpdateActorsIndexTask.cs`](../Jellyfin.Plugin.ActorsIndex/Tasks/UpdateActorsIndexTask.cs) and [`ActorsIndexMaintenanceService.cs`](../Jellyfin.Plugin.ActorsIndex/Services/ActorsIndexMaintenanceService.cs).

## 7. Live Library Monitoring

The monitor subscribes to movie, series, and episode add/update/remove events when the plugin and `MonitorLibraryChanges` setting are enabled.

```mermaid
sequenceDiagram
    participant Library as Jellyfin library manager
    participant Monitor as LibraryChangeMonitorService
    participant Accumulator as LibraryChangeAccumulator
    participant Maintenance as ActorsIndexMaintenanceService
    participant Store as SQLite store

    Library->>Monitor: ItemAdded / ItemUpdated / ItemRemoved
    Monitor->>Monitor: check settings and item type
    Monitor->>Accumulator: record normalized source item change
    Monitor->>Monitor: signal bounded channel
    Monitor->>Monitor: debounce for two seconds
    Monitor->>Accumulator: drain batch
    Monitor->>Maintenance: ProcessChangesAsync
    Maintenance->>Store: upsert current items and delete removed items
```

Removal wins when multiple event types for the same source item occur in one batch. Live batches do not advance the scheduled incremental watermark, so the daily task remains a reconciliation safety net. Sources: [`LibraryChangeMonitorService.cs`](../Jellyfin.Plugin.ActorsIndex/Services/LibraryChangeMonitorService.cs) and [`LibraryChangeAccumulator.cs`](../Jellyfin.Plugin.ActorsIndex/Services/LibraryChangeAccumulator.cs).

## 8. Actor Page and Actor Paging API

```mermaid
sequenceDiagram
    participant User as Signed-in user
    participant SPA as actorsBrowse.html
    participant API as ActorsIndexController
    participant Service as ActorsIndexService
    participant Jellyfin as Jellyfin library manager
    participant Store as SQLite store

    User->>SPA: Open Trombee or change search/filter/page
    SPA->>API: GET /Trombee/actors-index?startIndex=0&limit=60
    API->>API: clamp paging and parse sort, type, libraries
    API->>Service: GetActorsIndexAsync(user, query)
    Service->>Jellyfin: get currently visible source item IDs
    Service->>Store: QueryActorsAsync(active generation, visibility IDs)
    Store-->>API: ActorsPage
    API-->>SPA: camelCase paged JSON
    SPA-->>User: render stable 60-card grid and pager
```

The SPA owns route/history behavior and responsive presentation. The controller and store own validation, authorization, filtering, sorting, and paging. Sources: [`actorsBrowse.html`](../Jellyfin.Plugin.ActorsIndex/Configuration/actorsBrowse.html), [`ActorsIndexController.cs`](../Jellyfin.Plugin.ActorsIndex/Api/ActorsIndexController.cs), and [`ActorsIndexService.cs`](../Jellyfin.Plugin.ActorsIndex/Services/ActorsIndexService.cs).

## 9. Filmography Request

```mermaid
sequenceDiagram
    participant User
    participant SPA as actorsBrowse.html
    participant API as ActorsIndexController
    participant Service as ActorsIndexService
    participant Store as SQLite store

    User->>SPA: Open actor detail
    SPA->>API: GET /Trombee/actors/{actorKey}/items?startIndex=0&limit=60
    API->>Service: GetFilmographyAsync
    Service->>Store: QueryFilmographyAsync
    Store->>Store: filter by generation and actor_key
    Store->>Store: group episodes by display series ID
    Store-->>SPA: FilmographyPage
    SPA-->>User: modal, biography, roles, poster grid, load more
```

The credit index begins with `(generation_id, actor_key)`, matching the most selective filmography predicate. Episodes store their series as `display_item_id`, so a series appears once rather than once per episode.

## 10. Settings and Administrative Actions

```mermaid
flowchart LR
    Admin[Administrator settings page] --> Config[Jellyfin plugin configuration API]
    Admin --> Refresh[POST /Trombee/refresh-people]
    Admin --> Check[GET /Trombee/check-update]
    Admin --> Update[POST /Trombee/self-update]
    Config --> Instance[Plugin.Instance.Configuration]
    Refresh --> Providers[Jellyfin metadata provider queue]
    Check --> Manifest[xiakeng manifest.json]
    Update --> Manifest
    Update --> Files[Plugin DLL, JSON, and PNG files]
```

The controller requires Jellyfin elevation for image refresh and self-update operations. Image refresh queues a full metadata and image replacement for every Person item and is intentionally separate from actor index maintenance.

Sources: [`configPage.html`](../Jellyfin.Plugin.ActorsIndex/Configuration/configPage.html) and [`ActorsIndexController.cs`](../Jellyfin.Plugin.ActorsIndex/Api/ActorsIndexController.cs).

## 11. Plugin Pages Registration

```mermaid
flowchart TD
    Start[Hosted service starts] --> Locate[Search loaded assembly contexts]
    Locate --> Found{Plugin Pages assembly found?}
    Found -- No --> Direct[Keep authenticated direct route only]
    Found -- Yes --> Resolve[Resolve manager and page types by reflection]
    Resolve --> Manager[Resolve manager from DI]
    Manager --> Register[Register /Trombee/Pages/Browse]
    Register --> Menu[Non-admin user menu entry]
```

Reflection avoids a direct plugin dependency across isolated assembly load contexts. Source: [`PluginPagesRegistrationService.cs`](../Jellyfin.Plugin.ActorsIndex/Services/PluginPagesRegistrationService.cs).
