# SQL Resource and Schema Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move Trombee's SQLite schema and complex queries into embedded SQL resources, recreate incompatible derived databases from one composite schema hash, and automatically queue the full rebuild task for new or recreated databases.

**Architecture:** An embedded SQL provider owns deterministic schema/query resource loading and the composite SHA-256 schema hash. `SqliteActorsIndexStore` serializes initialization, creates replacement databases from schema resources, and returns a lifecycle result. A startup hosted service queues Jellyfin's existing `RebuildActorsIndexTask` after application startup when initialization created or recreated the database.

**Tech Stack:** .NET 9, Microsoft.Data.Sqlite 9.0.11, Jellyfin 10.11 task APIs, xUnit, Moq, embedded MSBuild resources.

---

### Task 1: Embedded Schema Resources and Composite Hash

**Files:**
- Create: `Jellyfin.Plugin.ActorsIndex/Persistence/Sql/SqlResource.cs`
- Create: `Jellyfin.Plugin.ActorsIndex/Persistence/Sql/ISqlResourceProvider.cs`
- Create: `Jellyfin.Plugin.ActorsIndex/Persistence/Sql/EmbeddedSqlResourceProvider.cs`
- Create: `Jellyfin.Plugin.ActorsIndex/Persistence/Sql/Schemas/TableSchemaMetadata.sql`
- Create: `Jellyfin.Plugin.ActorsIndex/Persistence/Sql/Schemas/TableIndexGenerations.sql`
- Create: `Jellyfin.Plugin.ActorsIndex/Persistence/Sql/Schemas/TableIndexState.sql`
- Create: `Jellyfin.Plugin.ActorsIndex/Persistence/Sql/Schemas/TableMediaItems.sql`
- Create: `Jellyfin.Plugin.ActorsIndex/Persistence/Sql/Schemas/TableMediaItemLibraries.sql`
- Create: `Jellyfin.Plugin.ActorsIndex/Persistence/Sql/Schemas/TableCredits.sql`
- Modify: `Jellyfin.Plugin.ActorsIndex/Jellyfin.Plugin.ActorsIndex.csproj`
- Test: `Jellyfin.Plugin.ActorsIndex.Tests/Persistence/EmbeddedSqlResourceProviderTests.cs`

- [ ] **Step 1: Write failing resource tests**

Add tests that construct `EmbeddedSqlResourceProvider` from the plugin assembly, assert the six ordered `Table*.sql` resources are present, and prove the composite hash is one stable 64-character hexadecimal value. Add a fake provider test that changes one schema resource and asserts the overall hash changes. Query-resource assertions are added only after those files are introduced in Task 4.

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test Jellyfin.Plugin.ActorsIndex.Tests\Jellyfin.Plugin.ActorsIndex.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~EmbeddedSqlResourceProviderTests"
```

Expected: compilation fails because the provider types do not exist.

- [ ] **Step 3: Implement resource types and embed SQL files**

Define this contract:

```csharp
public sealed record SqlResource(string Name, string Content);

public interface ISqlResourceProvider
{
    IReadOnlyList<SqlResource> SchemaResources { get; }

    string SchemaHash { get; }

    string GetQuery(string fileName);
}
```

`EmbeddedSqlResourceProvider` must sort schemas with `StringComparer.Ordinal`, normalize `\r\n` and `\r` to `\n`, calculate one SHA-256 value over every schema resource name and normalized content, cache query text, and throw `InvalidOperationException` for missing or empty resources.

Move each existing `CREATE TABLE` statement and its owned indexes into the approved `Table*.sql` file. Add this MSBuild item:

```xml
<EmbeddedResource Include="Persistence\Sql\**\*.sql" />
```

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the command from Step 2. Expected: all provider tests pass.

### Task 2: Hash-Aware Database Creation and Replacement

**Files:**
- Create: `Jellyfin.Plugin.ActorsIndex/Persistence/SchemaInitializationResult.cs`
- Modify: `Jellyfin.Plugin.ActorsIndex/Persistence/SqliteActorsIndexStore.cs`
- Test: `Jellyfin.Plugin.ActorsIndex.Tests/Persistence/SqliteActorsIndexStoreTests.cs`

- [ ] **Step 1: Write failing initialization lifecycle tests**

Add tests for these exact outcomes:

```csharp
Assert.Equal(SchemaInitializationResult.Created, await firstStore.InitializeAsync(token));
Assert.Equal(SchemaInitializationResult.Unchanged, await reopenedStore.InitializeAsync(token));
Assert.Equal(SchemaInitializationResult.Recreated, await mismatchedStore.InitializeAsync(token));
```

For mismatch, update `schema_metadata.schema_hash` to `different` before opening `mismatchedStore`, seed an actor first, and assert that actor data is gone after recreation. For failed replacement, inject a provider whose `TableCredits.sql` contains invalid SQL, set the existing hash to `different`, assert initialization throws, then reopen with the valid provider and assert the seeded actor remains.

- [ ] **Step 2: Run lifecycle tests and verify RED**

Run:

```powershell
dotnet test Jellyfin.Plugin.ActorsIndex.Tests\Jellyfin.Plugin.ActorsIndex.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Schema"
```

Expected: compilation fails because `InitializeAsync` does not return a lifecycle result and the injected resource-provider constructor does not exist.

- [ ] **Step 3: Implement serialized initialization**

Add:

```csharp
public enum SchemaInitializationResult
{
    Unchanged,
    Created,
    Recreated
}
```

Store the absolute database path and `ISqlResourceProvider`. Preserve the one-argument constructor by creating an `EmbeddedSqlResourceProvider` from the plugin assembly; add a provider constructor for tests and dependency injection. Serialize initialization with a dedicated semaphore and cache the first successful result for the process.

When no database exists, create a temporary database in the same directory, execute all schema resources plus the singleton hash insert in one transaction, close it, and move it into place. When the stored hash is missing or differs, build the temporary database first, move the existing database to a backup, move the replacement into place, remove stale `-wal`/`-shm` sidecars, and delete the backup. Restore the backup if replacement fails. Never modify the target before temporary schema creation succeeds.

Remove the in-code schema DDL from `InitializeAsync`.

- [ ] **Step 4: Run lifecycle and existing persistence tests**

Run:

```powershell
dotnet test Jellyfin.Plugin.ActorsIndex.Tests\Jellyfin.Plugin.ActorsIndex.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Persistence"
```

Expected: all persistence tests pass.

### Task 3: Automatic Scheduled Rebuild

**Files:**
- Create: `Jellyfin.Plugin.ActorsIndex/Services/ActorsIndexInitializationService.cs`
- Modify: `Jellyfin.Plugin.ActorsIndex/Services/LibraryChangeMonitorService.cs`
- Modify: `Jellyfin.Plugin.ActorsIndex/PluginServiceRegistrator.cs`
- Test: `Jellyfin.Plugin.ActorsIndex.Tests/Services/ActorsIndexInitializationServiceTests.cs`

- [ ] **Step 1: Write failing queue-behavior tests**

Use a temporary database, a fake `IHostApplicationLifetime`, and `Mock<ITaskManager>`. Verify that firing `ApplicationStarted` calls:

```csharp
taskManager.Verify(manager => manager.QueueIfNotRunning<RebuildActorsIndexTask>(), Times.Once);
```

for both `Created` and `Recreated`, and never for `Unchanged`. Also assert no queue occurs if initialization throws.

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
dotnet test Jellyfin.Plugin.ActorsIndex.Tests\Jellyfin.Plugin.ActorsIndex.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ActorsIndexInitializationServiceTests"
```

Expected: compilation fails because the hosted service does not exist.

- [ ] **Step 3: Implement startup coordination**

Create an `IHostedService` that initializes the store in `StartAsync`. For `Created` or `Recreated`, register one `ApplicationStarted` callback that invokes `ITaskManager.QueueIfNotRunning<RebuildActorsIndexTask>()`; dispose the registration in `StopAsync`. Log creation, recreation, queueing, and failures with source-generated `LoggerMessage` delegates.

Register this hosted service before `LibraryChangeMonitorService`. Keep the monitor's idempotent store initialization so it subscribes only after the schema is usable. Keep `RebuildActorsIndexTask.GetDefaultTriggers()` empty.

- [ ] **Step 4: Run startup and task tests**

Run:

```powershell
dotnet test Jellyfin.Plugin.ActorsIndex.Tests\Jellyfin.Plugin.ActorsIndex.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~InitializationService|FullyQualifiedName~ScheduledTask"
```

Expected: all matching tests pass.

### Task 4: Extract Complex Queries

**Files:**
- Create: `Jellyfin.Plugin.ActorsIndex/Persistence/Sql/Queries/QueryActorsCount.sql`
- Create: `Jellyfin.Plugin.ActorsIndex/Persistence/Sql/Queries/QueryActorsPage.sql`
- Create: `Jellyfin.Plugin.ActorsIndex/Persistence/Sql/Queries/QueryFilmographyCount.sql`
- Create: `Jellyfin.Plugin.ActorsIndex/Persistence/Sql/Queries/QueryFilmographyPage.sql`
- Modify: `Jellyfin.Plugin.ActorsIndex/Persistence/SqliteActorsIndexStore.cs`
- Test: `Jellyfin.Plugin.ActorsIndex.Tests/Persistence/EmbeddedSqlResourceProviderTests.cs`
- Test: `Jellyfin.Plugin.ActorsIndex.Tests/Persistence/SqliteActorsIndexStoreTests.cs`

- [ ] **Step 1: Extend resource tests and verify RED**

Assert each exact query filename resolves to non-empty SQL containing its terminal command: `COUNT(*)`, actor `LIMIT $limit OFFSET $startIndex`, filmography `COUNT(*)`, and filmography `LIMIT $limit OFFSET $startIndex`. Run the provider tests and confirm failure while resources are absent.

- [ ] **Step 2: Create four self-contained query files**

Copy the complete actor CTE into both actor files and terminate one with the count selection and the other with deterministic sort/paging. Copy the complete filmography CTE into both filmography files and terminate one with count and the other with deterministic sort/paging. Do not introduce shared SQL fragments or UDFs.

- [ ] **Step 3: Replace C# query constants**

Remove `ActorCountsCte` and `FilmographyCte`. Set command text through:

```csharp
countCommand.CommandText = _sqlResources.GetQuery("QueryActorsCount.sql");
pageCommand.CommandText = _sqlResources.GetQuery("QueryActorsPage.sql");
```

and the corresponding filmography filenames. Keep parameter binding in the existing C# helpers.

- [ ] **Step 4: Run query and full test suites**

Run:

```powershell
dotnet test Jellyfin.Plugin.ActorsIndex.Tests\Jellyfin.Plugin.ActorsIndex.Tests.csproj -c Release --no-restore
```

Expected: all tests pass with zero failures.

### Task 5: Verification and Deployment Resume

**Files:**
- Verify all changed production, test, SQL, and documentation files.

- [ ] **Step 1: Run clean build and publish**

```powershell
dotnet build Jellyfin.Plugin.ActorsIndex.sln -c Release --no-restore
dotnet publish Jellyfin.Plugin.ActorsIndex\Jellyfin.Plugin.ActorsIndex.csproj -c Release --no-restore
git diff --check
```

Expected: zero warnings, zero errors, and no whitespace errors.

- [ ] **Step 2: Inspect embedded SQL resources**

Verify `Jellyfin.Plugin.Trombee.dll` contains all six schema and four query resource names and that no complex CTE or schema DDL remains in `SqliteActorsIndexStore.cs`.

- [ ] **Step 3: Resume the agreed manual-stop deployment flow**

Confirm `jellyfin.exe` is stopped over SSH, copy the published DLL to `C:\ProgramData\Jellyfin\Server\plugins\Trombee_1.0.3.1`, ask the user to start Jellyfin, and verify logs show database creation/recreation plus the automatically queued full rebuild.

- [ ] **Step 4: Complete E2E verification**

Verify persisted actor paging, filmography paging, scheduled-task visibility/history, schema hash storage, restart persistence, immediate library-change processing when enabled, and rendered desktop/mobile Trombee flows with the in-app Browser.

### Task 6: Align Schema Names and Actor Lookup Indexes

**Files:**
- Rename: `Jellyfin.Plugin.ActorsIndex/Persistence/Sql/Schemas/TableCredits.sql` to `credits.sql`
- Rename: `Jellyfin.Plugin.ActorsIndex/Persistence/Sql/Schemas/TableIndexGenerations.sql` to `index_generations.sql`
- Rename: `Jellyfin.Plugin.ActorsIndex/Persistence/Sql/Schemas/TableIndexState.sql` to `index_state.sql`
- Rename: `Jellyfin.Plugin.ActorsIndex/Persistence/Sql/Schemas/TableMediaItemLibraries.sql` to `media_item_libraries.sql`
- Rename: `Jellyfin.Plugin.ActorsIndex/Persistence/Sql/Schemas/TableMediaItems.sql` to `media_items.sql`
- Rename: `Jellyfin.Plugin.ActorsIndex/Persistence/Sql/Schemas/TableSchemaMetadata.sql` to `schema_metadata.sql`
- Modify: `Jellyfin.Plugin.ActorsIndex/Persistence/Sql/Schemas/credits.sql`
- Modify: `Jellyfin.Plugin.ActorsIndex/Persistence/SqliteActorsIndexStore.cs`
- Test: `Jellyfin.Plugin.ActorsIndex.Tests/Persistence/EmbeddedSqlResourceProviderTests.cs`
- Test: `Jellyfin.Plugin.ActorsIndex.Tests/Persistence/SqliteActorsIndexStoreTests.cs`
- Test: invalid schema providers under `Jellyfin.Plugin.ActorsIndex.Tests`

- [x] **Step 1: Write failing schema contract tests**

Change the resource-name assertion to the exact lowercase table names. Add a persistence test that reads `PRAGMA index_info('idx_credits_actor_query')` and expects:

```text
generation_id
actor_key
person_type
source_item_id
```

Add a persistence test that completes a rebuild and asserts `sqlite_stat1` contains statistics for `credits` and `media_items`.

- [x] **Step 2: Run focused tests and verify RED**

Run:

```powershell
dotnet test Jellyfin.Plugin.ActorsIndex.Tests\Jellyfin.Plugin.ActorsIndex.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~EmbeddedSqlResourceProviderTests|FullyQualifiedName~SchemaUsesFilmographyIndexOrder|FullyQualifiedName~CompletedRebuildUpdatesPlannerStatistics"
```

Expected: resource names retain the `Table` prefix, the actor index reports `person_type` before `actor_key`, and no `sqlite_stat1` statistics exist.

- [x] **Step 3: Rename schemas and correct the credits lookup index**

Use table-name filenames exactly. Change the secondary credits index to:

```sql
CREATE INDEX idx_credits_actor_query
    ON credits (generation_id, actor_key, person_type, source_item_id);
```

Keep all generation-leading primary keys unchanged. Update invalid-resource test providers to target `credits.sql`.

- [x] **Step 4: Maintain SQLite planner statistics after scheduled maintenance**

After a full rebuild activates and after a daily incremental run completes, execute:

```sql
PRAGMA optimize=0x10002;
```

Run it on the existing writer connection after the state transaction commits, while the writer gate remains held. Do not run it for each live library event.

- [x] **Step 5: Run focused and full verification**

Run:

```powershell
dotnet test Jellyfin.Plugin.ActorsIndex.Tests\Jellyfin.Plugin.ActorsIndex.Tests.csproj -c Release --no-restore
dotnet build Jellyfin.Plugin.ActorsIndex.sln -c Release --no-restore
git diff --check
```

Expected: all tests pass, build completes with zero warnings and errors, and the diff has no whitespace errors.

### Task 7: Replace Startup Polling with a Hidden Bootstrap Task

This task supersedes Task 3's application-lifetime queue coordination after runtime testing exposed Jellyfin's scheduled-task registration order.

**Files:**
- Create: `Jellyfin.Plugin.ActorsIndex/Tasks/ActorsIndexBootstrapTask.cs`
- Modify: `Jellyfin.Plugin.ActorsIndex/Services/ActorsIndexInitializationService.cs`
- Modify: `Jellyfin.Plugin.ActorsIndex/PluginServiceRegistrator.cs`
- Modify: `Jellyfin.Plugin.ActorsIndex.Tests/Tasks/ActorsIndexScheduledTaskTests.cs`
- Modify: `Jellyfin.Plugin.ActorsIndex.Tests/Services/ActorsIndexInitializationServiceTests.cs`

- [ ] **Step 1: Write failing hidden-task lifecycle tests**

Add tests that require `ActorsIndexBootstrapTask` to implement `IConfigurableScheduledTask`, remain hidden, and expose exactly one `StartupTrigger`. With a real temporary SQLite store, verify that execution queues `RebuildActorsIndexTask` when `active_generation` is null and does not queue it after a rebuild generation is activated. Replace initialization-service queue tests with a focused test proving that the hosted service creates the schema without depending on application-lifetime or task-manager timing.

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
dotnet test Jellyfin.Plugin.ActorsIndex.Tests\Jellyfin.Plugin.ActorsIndex.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ActorsIndexInitializationServiceTests|FullyQualifiedName~ActorsIndexScheduledTaskTests"
```

Expected: compilation fails because `ActorsIndexBootstrapTask` does not exist and the initialization-service constructor still requires `ITaskManager` and `IHostApplicationLifetime`.

- [ ] **Step 3: Implement native startup coordination**

Create the hidden task with these scheduling properties:

```csharp
public bool IsHidden => true;
public bool IsEnabled => true;
public bool IsLogged => true;

public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
{
    return [new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger }];
}
```

Its `ExecuteAsync` method must call `SqliteActorsIndexStore.HasActiveGenerationAsync`. When the result is false, queue `RebuildActorsIndexTask` through `ITaskManager.QueueIfNotRunning`; otherwise return without queueing. Keep the readiness decision durable by deriving it from SQLite rather than process memory.

Remove `IHostApplicationLifetime`, `ITaskManager`, the application-start callback, polling interval, timeout, and background queue task from `ActorsIndexInitializationService`. Keep database initialization and failure logging. Register the hidden task as an `IScheduledTask` while leaving `RebuildActorsIndexTask.GetDefaultTriggers()` empty.

- [ ] **Step 4: Run focused and full verification**

Run:

```powershell
dotnet test Jellyfin.Plugin.ActorsIndex.Tests\Jellyfin.Plugin.ActorsIndex.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ActorsIndexInitializationServiceTests|FullyQualifiedName~ActorsIndexScheduledTaskTests"
dotnet test Jellyfin.Plugin.ActorsIndex.Tests\Jellyfin.Plugin.ActorsIndex.Tests.csproj -c Release --no-restore
dotnet build Jellyfin.Plugin.ActorsIndex.sln -c Release --no-restore
git diff --check
```

Expected: all tests pass, the build has zero warnings and errors, and no whitespace errors are reported.
