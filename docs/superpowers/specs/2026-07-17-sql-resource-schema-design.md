# SQL Resource and Schema Lifecycle Design

## Scope

Refactor Trombee's plugin-owned SQLite implementation so schema definitions and complex queries are embedded `.sql` resources rather than large C# string constants. The actors index remains derived data that can be recreated from Jellyfin.

## Resource Layout

```text
Persistence/Sql/
  Schemas/
    credits.sql
    index_generations.sql
    index_state.sql
    media_item_libraries.sql
    media_items.sql
    schema_metadata.sql
  Queries/
    QueryActorsCount.sql
    QueryActorsPage.sql
    QueryFilmographyCount.sql
    QueryFilmographyPage.sql
```

Each schema file has the same name as its table and owns that table, its constraints, and its related indexes. Foreign keys are declared by the child table that owns the constraint. Schema resources are loaded in deterministic ordinal filename order.

Each query resource is a complete independently executable SQL command. The count and page commands intentionally repeat their filtering CTEs. The store will not concatenate shared SQL fragments at runtime. Simple single-table CRUD statements and temporary filter-table operations remain inline in C#.

All SQL files are embedded in `Jellyfin.Plugin.Trombee.dll`; deployment remains a single plugin assembly plus existing metadata and image files.

## Resource Loading

A small SQL resource loader reads embedded resources by logical name and caches their text. Missing, duplicate, or empty resources fail initialization with a descriptive exception. Query commands retrieve their complete SQL text through this loader.

SQLite application-defined functions are not used. `Microsoft.Data.Sqlite` functions return scalar or aggregate values and do not replace parameterized joins, grouping, permission filters, or paging queries.

## Composite Schema Hash

The plugin calculates one SHA-256 hash for the entire schema set. It sorts schema resources by ordinal logical name, normalizes line endings, and hashes each filename and content into one deterministic composite value.

`schema_metadata.sql` creates a singleton metadata table that stores the composite hash used to create the database. The metadata table is itself part of the hashed schema set.

## Initialization and Replacement

Initialization is serialized so concurrent API, hosted-service, and scheduled-task calls cannot create or replace the database simultaneously. It returns one of three outcomes:

- `Unchanged`: the existing database contains the expected composite hash.
- `Created`: no database existed and a new empty database was created.
- `Recreated`: an existing database had no schema metadata or a different composite hash and was replaced.

For `Created` and `Recreated`, the plugin builds a temporary database in the same data directory, executes all schema files in one transaction, writes the composite hash, and validates that the transaction completed. Only then does it replace the target database. SQLite WAL and shared-memory sidecar files belonging to the incompatible database are removed during replacement.

If temporary database creation fails, the existing database remains untouched, initialization reports the failure, and no rebuild is queued.

The current in-code-schema database has no schema metadata. On the first deployment of this refactor it is therefore treated as incompatible, recreated, and rebuilt automatically.

## Automatic Rebuild

A startup coordinator initializes the store before the library-change monitor begins accepting events. After Jellyfin finishes starting, the coordinator uses `ITaskManager.QueueIfNotRunning<RebuildActorsIndexTask>()` when initialization returned either `Created` or `Recreated`.

This programmatic execution preserves Jellyfin scheduled-task progress, cancellation, logging, and history. `RebuildActorsIndexTask.GetDefaultTriggers()` remains empty, so no recurring trigger is added. A matching existing schema does not queue a rebuild.

## Query Behavior

`QueryActorsCount.sql` and `QueryActorsPage.sql` each contain the complete actor filtering and aggregation CTE. Both apply person type, literal search, selected-library, visible-item, and minimum-appearance filters before count or paging.

`QueryFilmographyCount.sql` and `QueryFilmographyPage.sql` each contain the complete actor filmography CTE. Both apply actor key, person type, selected-library, and visible-item filters before count or paging. The page query retains deterministic year, name, and item-ID ordering.

The existing parameter binding methods remain in C# and are shared by each count/page pair, keeping their parameter contracts consistent.

The credits query index is ordered `(generation_id, actor_key, person_type, source_item_id)`. Filmography queries can therefore seek directly on the active generation and actor key, while actor-list queries can still scan only the active generation. Generation-leading primary keys remain responsible for snapshot coexistence, parent-child joins, reconciliation, and cascade cleanup.

After successful full-rebuild activation and daily incremental completion, the store runs `PRAGMA optimize=0x10002`. This gives SQLite current planner statistics after substantial scheduled changes without adding optimization work to each live library event.

## Verification

Automated tests will cover:

- deterministic loading and one composite hash for all schema resources;
- exact table-name schema resource filenames;
- creation of every table, constraint, and index from embedded schema files;
- the filmography-oriented credits index column order and planner statistics maintenance;
- reuse of a database whose schema hash matches;
- automatic rebuild queuing for a missing database;
- temporary replacement and rebuild queuing for a missing or changed hash;
- preservation of the existing database when replacement creation fails;
- no rebuild queue for an unchanged schema;
- loading each complex query from its own resource;
- existing actor and filmography filtering, permission, sorting, counting, and paging behavior;
- the full rebuild task retaining no default triggers.

Deployment verification will confirm database creation, automatic task execution, persisted data after restart, paged APIs, live library changes, and the rendered Trombee page.
