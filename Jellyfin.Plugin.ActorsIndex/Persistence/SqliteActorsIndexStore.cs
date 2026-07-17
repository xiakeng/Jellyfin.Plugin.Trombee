using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trombee.Persistence.Sql;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Trombee.Persistence;

/// <summary>
/// Stores the durable Trombee actors index in SQLite.
/// </summary>
public sealed class SqliteActorsIndexStore : IAsyncDisposable
{
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly ISqlResourceProvider _sqlResources;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SemaphoreSlim _writerGate = new(1, 1);
    private bool _isInitialized;
    private SchemaInitializationResult _initializationResult;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteActorsIndexStore"/> class.
    /// </summary>
    /// <param name="databasePath">The absolute path to the SQLite database.</param>
    public SqliteActorsIndexStore(string databasePath)
        : this(databasePath, new EmbeddedSqlResourceProvider(typeof(SqliteActorsIndexStore).Assembly))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteActorsIndexStore"/> class.
    /// </summary>
    /// <param name="databasePath">The absolute path to the SQLite database.</param>
    /// <param name="sqlResources">The schema and query SQL resources.</param>
    public SqliteActorsIndexStore(string databasePath, ISqlResourceProvider sqlResources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(sqlResources);

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("The database path must include a directory.", nameof(databasePath));
        }

        Directory.CreateDirectory(directory);
        _databasePath = fullPath;
        _connectionString = CreateConnectionString(fullPath);
        _sqlResources = sqlResources;
    }

    /// <summary>
    /// Creates the database schema when it does not already exist.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task<SchemaInitializationResult> InitializeAsync(CancellationToken cancellationToken)
    {
        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isInitialized)
            {
                return _initializationResult;
            }

            var databaseExists = File.Exists(_databasePath);
            if (databaseExists)
            {
                var storedHash = await GetStoredSchemaHashAsync(cancellationToken).ConfigureAwait(false);
                if (string.Equals(storedHash, _sqlResources.SchemaHash, StringComparison.Ordinal))
                {
                    _initializationResult = SchemaInitializationResult.Unchanged;
                    _isInitialized = true;
                    return _initializationResult;
                }
            }

            var temporaryPath = CreateTemporaryDatabasePath();
            try
            {
                await CreateSchemaDatabaseAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
                if (databaseExists)
                {
                    ReplaceDatabase(temporaryPath);
                }
                else
                {
                    File.Move(temporaryPath, _databasePath);
                }
            }
            finally
            {
                DeleteFileIfExists(temporaryPath);
            }

            _initializationResult = databaseExists
                ? SchemaInitializationResult.Recreated
                : SchemaInitializationResult.Created;
            _isInitialized = true;
            return _initializationResult;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    /// <summary>
    /// Gets a value indicating whether a completed index generation is active.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns><see langword="true"/> when an active generation exists.</returns>
    public async Task<bool> HasActiveGenerationAsync(CancellationToken cancellationToken)
    {
        return await GetActiveGenerationIdAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    /// <summary>
    /// Gets the active generation identifier.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The active generation identifier, or <see langword="null"/> when the index is empty.</returns>
    public async Task<long?> GetActiveGenerationIdAsync(CancellationToken cancellationToken)
    {
        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT active_generation FROM index_state WHERE singleton_id = 1;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null || result == DBNull.Value
            ? null
            : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Starts an inactive full-rebuild generation.
    /// </summary>
    /// <param name="startedUtc">The rebuild start time.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The new generation identifier.</returns>
    public async Task<long> BeginRebuildAsync(DateTimeOffset startedUtc, CancellationToken cancellationToken)
    {
        await _writerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    DELETE FROM index_generations WHERE status = 'building';

                    INSERT INTO index_generations (status, created_utc)
                    VALUES ('building', $createdUtc);

                    SELECT last_insert_rowid();
                    """;
                command.Parameters.AddWithValue("$createdUtc", FormatTimestamp(startedUtc));
                var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                var generationId = Convert.ToInt64(result, CultureInfo.InvariantCulture);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return generationId;
            }
        }
        finally
        {
            _writerGate.Release();
        }
    }

    /// <summary>
    /// Atomically marks a completed rebuild as the active generation.
    /// </summary>
    /// <param name="generationId">The generation to activate.</param>
    /// <param name="completedUtc">The successful completion time.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task representing the operation.</returns>
    public Task ActivateRebuildAsync(long generationId, DateTimeOffset completedUtc, CancellationToken cancellationToken)
    {
        return ActivateRebuildAsync(generationId, completedUtc, completedUtc, cancellationToken);
    }

    /// <summary>
    /// Atomically marks a completed rebuild as the active generation with an independent incremental watermark.
    /// </summary>
    /// <param name="generationId">The generation to activate.</param>
    /// <param name="completedUtc">The successful completion time.</param>
    /// <param name="incrementalWatermarkUtc">The earliest timestamp that the next incremental run must revisit.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task ActivateRebuildAsync(
        long generationId,
        DateTimeOffset completedUtc,
        DateTimeOffset incrementalWatermarkUtc,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generationId);

        await _writerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                using var completeCommand = connection.CreateCommand();
                completeCommand.Transaction = transaction;
                completeCommand.CommandText =
                    """
                    UPDATE index_generations
                    SET status = 'completed', completed_utc = $completedUtc
                    WHERE generation_id = $generationId AND status = 'building';
                    """;
                completeCommand.Parameters.AddWithValue("$completedUtc", FormatTimestamp(completedUtc));
                completeCommand.Parameters.AddWithValue("$generationId", generationId);
                var changed = await completeCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                if (changed != 1)
                {
                    throw new InvalidOperationException("Only a building generation can be activated.");
                }

                using var activateCommand = connection.CreateCommand();
                activateCommand.Transaction = transaction;
                activateCommand.CommandText =
                    """
                    UPDATE index_state
                    SET active_generation = $generationId,
                        last_success_utc = $completedUtc,
                        last_incremental_watermark_utc = $incrementalWatermarkUtc
                    WHERE singleton_id = 1;
                    """;
                activateCommand.Parameters.AddWithValue("$generationId", generationId);
                activateCommand.Parameters.AddWithValue("$completedUtc", FormatTimestamp(completedUtc));
                activateCommand.Parameters.AddWithValue("$incrementalWatermarkUtc", FormatTimestamp(incrementalWatermarkUtc));
                _ = await activateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                using var cleanupCommand = connection.CreateCommand();
                cleanupCommand.Transaction = transaction;
                cleanupCommand.CommandText =
                    "DELETE FROM index_generations WHERE generation_id <> $generationId;";
                cleanupCommand.Parameters.AddWithValue("$generationId", generationId);
                _ = await cleanupCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _writerGate.Release();
        }
    }

    /// <summary>
    /// Replaces a batch of source items within a generation.
    /// </summary>
    /// <param name="generationId">The generation receiving the items.</param>
    /// <param name="items">The source items to replace.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task ReplaceItemsAsync(
        long generationId,
        IReadOnlyCollection<IndexedMediaItem> items,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generationId);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return;
        }

        await _writerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await ReplaceItemAsync(connection, transaction, generationId, item, cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _writerGate.Release();
        }
    }

    /// <summary>
    /// Deletes source items from a generation.
    /// </summary>
    /// <param name="generationId">The generation containing the items.</param>
    /// <param name="sourceItemIds">The source item identifiers to delete.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task DeleteItemsAsync(
        long generationId,
        IReadOnlyCollection<Guid> sourceItemIds,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generationId);
        ArgumentNullException.ThrowIfNull(sourceItemIds);
        if (sourceItemIds.Count == 0)
        {
            return;
        }

        await _writerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    "DELETE FROM media_items WHERE generation_id = $generationId AND source_item_id = $sourceItemId;";
                command.Parameters.AddWithValue("$generationId", generationId);
                var sourceItemIdParameter = command.Parameters.Add("$sourceItemId", SqliteType.Text);
                foreach (var sourceItemId in sourceItemIds.Distinct())
                {
                    sourceItemIdParameter.Value = sourceItemId.ToString("N", CultureInfo.InvariantCulture);
                    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _writerGate.Release();
        }
    }

    /// <summary>
    /// Marks an incremental maintenance run as successful.
    /// </summary>
    /// <param name="completedUtc">The successful completion watermark.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task CompleteIncrementalAsync(DateTimeOffset completedUtc, CancellationToken cancellationToken)
    {
        await _writerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE index_state
                SET last_success_utc = $completedUtc,
                    last_incremental_watermark_utc = $completedUtc
                WHERE singleton_id = 1 AND active_generation IS NOT NULL;
                """;
            command.Parameters.AddWithValue("$completedUtc", FormatTimestamp(completedUtc));
            var changed = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (changed != 1)
            {
                throw new InvalidOperationException("An active generation is required for incremental maintenance.");
            }
        }
        finally
        {
            _writerGate.Release();
        }
    }

    /// <summary>
    /// Gets the last successful incremental maintenance watermark.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The watermark, or <see langword="null"/> before the first successful index build.</returns>
    public async Task<DateTimeOffset?> GetIncrementalWatermarkAsync(CancellationToken cancellationToken)
    {
        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_incremental_watermark_utc FROM index_state WHERE singleton_id = 1;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null || result == DBNull.Value
            ? null
            : DateTimeOffset.Parse((string)result, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    /// <summary>
    /// Gets all source item identifiers stored in a generation.
    /// </summary>
    /// <param name="generationId">The generation to inspect.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The stored source item identifiers.</returns>
    public async Task<IReadOnlyList<Guid>> GetSourceItemIdsAsync(long generationId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generationId);

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT source_item_id
            FROM media_items
            WHERE generation_id = $generationId
            ORDER BY source_item_id;
            """;
        command.Parameters.AddWithValue("$generationId", generationId);
        var result = new List<Guid>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(Guid.ParseExact(reader.GetString(0), "N"));
        }

        return result;
    }

    /// <summary>
    /// Queries actor summaries from the active generation.
    /// </summary>
    /// <param name="query">The server-side filter, sort, and paging parameters.</param>
    /// <param name="visibleSourceItemIds">Source items visible to the caller, or <see langword="null"/> for unrestricted access.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The matching page and total record count.</returns>
    public async Task<ActorsPage> QueryActorsAsync(
        ActorsQuery query,
        IReadOnlyCollection<Guid>? visibleSourceItemIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(query.StartIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.Limit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.MinimumAppearances);

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var generationId = await GetActiveGenerationIdAsync(connection, cancellationToken).ConfigureAwait(false);
        if (generationId is null)
        {
            return new ActorsPage(0, query.StartIndex, query.Limit, []);
        }

        await CreateQueryFilterTablesAsync(connection, cancellationToken).ConfigureAwait(false);
        await InsertGuidFilterAsync(connection, "selected_libraries", "library_id", query.LibraryIds, cancellationToken).ConfigureAwait(false);
        if (visibleSourceItemIds is not null)
        {
            await InsertGuidFilterAsync(connection, "visible_source_items", "source_item_id", visibleSourceItemIds, cancellationToken).ConfigureAwait(false);
        }

        using var countCommand = connection.CreateCommand();
        SetEmbeddedQuery(countCommand, "QueryActorsCount.sql");
        AddActorQueryParameters(countCommand, generationId.Value, query, visibleSourceItemIds is not null);
        var totalResult = await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var totalRecordCount = Convert.ToInt32(totalResult, CultureInfo.InvariantCulture);

        using var pageCommand = connection.CreateCommand();
        SetEmbeddedQuery(pageCommand, "QueryActorsPage.sql");
        AddActorQueryParameters(pageCommand, generationId.Value, query, visibleSourceItemIds is not null);
        pageCommand.Parameters.AddWithValue("$limit", query.Limit);
        pageCommand.Parameters.AddWithValue("$startIndex", query.StartIndex);

        var actors = new List<ActorSummary>();
        using var reader = await pageCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var personIdText = await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false)
                ? null
                : reader.GetString(3);
            actors.Add(new ActorSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                Guid.TryParseExact(personIdText, "N", out var personId) ? personId : null));
        }

        return new ActorsPage(totalRecordCount, query.StartIndex, query.Limit, actors);
    }

    /// <summary>
    /// Queries filmography items for one actor from the active generation.
    /// </summary>
    /// <param name="actorKey">The stable actor key.</param>
    /// <param name="query">The server-side filter and paging parameters.</param>
    /// <param name="visibleSourceItemIds">Source items visible to the caller, or <see langword="null"/> for unrestricted access.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The matching filmography page and total record count.</returns>
    public async Task<FilmographyPage> QueryFilmographyAsync(
        string actorKey,
        FilmographyQuery query,
        IReadOnlyCollection<Guid>? visibleSourceItemIds,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorKey);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(query.StartIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(query.Limit);

        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var generationId = await GetActiveGenerationIdAsync(connection, cancellationToken).ConfigureAwait(false);
        if (generationId is null)
        {
            return new FilmographyPage(0, query.StartIndex, query.Limit, []);
        }

        await CreateQueryFilterTablesAsync(connection, cancellationToken).ConfigureAwait(false);
        await InsertGuidFilterAsync(connection, "selected_libraries", "library_id", query.LibraryIds, cancellationToken).ConfigureAwait(false);
        if (visibleSourceItemIds is not null)
        {
            await InsertGuidFilterAsync(connection, "visible_source_items", "source_item_id", visibleSourceItemIds, cancellationToken).ConfigureAwait(false);
        }

        using var countCommand = connection.CreateCommand();
        SetEmbeddedQuery(countCommand, "QueryFilmographyCount.sql");
        AddFilmographyParameters(countCommand, generationId.Value, actorKey, query, visibleSourceItemIds is not null);
        var totalResult = await countCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var totalRecordCount = Convert.ToInt32(totalResult, CultureInfo.InvariantCulture);

        using var pageCommand = connection.CreateCommand();
        SetEmbeddedQuery(pageCommand, "QueryFilmographyPage.sql");
        AddFilmographyParameters(pageCommand, generationId.Value, actorKey, query, visibleSourceItemIds is not null);
        pageCommand.Parameters.AddWithValue("$limit", query.Limit);
        pageCommand.Parameters.AddWithValue("$startIndex", query.StartIndex);

        var items = new List<FilmographyItem>();
        using var reader = await pageCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var year = await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false)
                ? (int?)null
                : reader.GetInt32(3);
            items.Add(new FilmographyItem(
                Guid.ParseExact(reader.GetString(0), "N"),
                reader.GetString(1),
                reader.GetString(2),
                year,
                reader.GetString(4)));
        }

        return new FilmographyPage(totalRecordCount, query.StartIndex, query.Limit, items);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _initializationGate.Dispose();
        _writerGate.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private static string CreateConnectionString(string databasePath)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string CreateTemporaryDatabasePath()
    {
        return _databasePath + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
    }

    private void ReplaceDatabase(string replacementPath)
    {
        var backupPath = _databasePath + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".bak";
        DeleteFileIfExists(_databasePath + "-wal");
        DeleteFileIfExists(_databasePath + "-shm");
        try
        {
            File.Replace(replacementPath, _databasePath, backupPath, ignoreMetadataErrors: true);
        }
        catch
        {
            if (!File.Exists(_databasePath) && File.Exists(backupPath))
            {
                File.Move(backupPath, _databasePath);
            }

            throw;
        }
        finally
        {
            DeleteFileIfExists(backupPath);
        }
    }

    private async Task CreateSchemaDatabaseAsync(string databasePath, CancellationToken cancellationToken)
    {
        using var connection = new SqliteConnection(CreateConnectionString(databasePath));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using (var pragmaCommand = connection.CreateCommand())
        {
            pragmaCommand.CommandText = "PRAGMA foreign_keys = ON;";
            _ = await pragmaCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            foreach (var resource in _sqlResources.SchemaResources)
            {
                using var schemaCommand = connection.CreateCommand();
                schemaCommand.Transaction = transaction;
#pragma warning disable CA2100 // Schema SQL is a trusted resource embedded at compile time.
                schemaCommand.CommandText = resource.Content;
#pragma warning restore CA2100
                _ = await schemaCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            using var metadataCommand = connection.CreateCommand();
            metadataCommand.Transaction = transaction;
            metadataCommand.CommandText =
                "INSERT INTO schema_metadata (singleton_id, schema_hash) VALUES (1, $schemaHash);";
            metadataCommand.Parameters.AddWithValue("$schemaHash", _sqlResources.SchemaHash);
            _ = await metadataCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string?> GetStoredSchemaHashAsync(CancellationToken cancellationToken)
    {
        using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using (var tableCommand = connection.CreateCommand())
        {
            tableCommand.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_metadata';";
            var tableCount = Convert.ToInt32(
                await tableCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                CultureInfo.InvariantCulture);
            if (tableCount == 0)
            {
                return null;
            }
        }

        using var hashCommand = connection.CreateCommand();
        hashCommand.CommandText = "SELECT schema_hash FROM schema_metadata WHERE singleton_id = 1;";
        return await hashCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static string EscapeLikePattern(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    private void SetEmbeddedQuery(SqliteCommand command, string fileName)
    {
#pragma warning disable CA2100 // Query SQL is a trusted resource embedded at compile time.
        command.CommandText = _sqlResources.GetQuery(fileName);
#pragma warning restore CA2100
    }

    private static void AddActorQueryParameters(
        SqliteCommand command,
        long generationId,
        ActorsQuery query,
        bool filterVisibleItems)
    {
        command.Parameters.AddWithValue("$generationId", generationId);
        command.Parameters.AddWithValue("$personType", query.PersonType);
        command.Parameters.AddWithValue(
            "$searchTerm",
            string.IsNullOrWhiteSpace(query.SearchTerm) ? DBNull.Value : EscapeLikePattern(query.SearchTerm.Trim()));
        command.Parameters.AddWithValue("$filterLibraries", query.LibraryIds.Count > 0 ? 1 : 0);
        command.Parameters.AddWithValue("$filterVisibleItems", filterVisibleItems ? 1 : 0);
        command.Parameters.AddWithValue("$minimumAppearances", query.MinimumAppearances);
        command.Parameters.AddWithValue("$sortBy", query.SortBy.ToString());
        command.Parameters.AddWithValue("$sortDirection", query.SortDirection.ToString());
    }

    private static void AddFilmographyParameters(
        SqliteCommand command,
        long generationId,
        string actorKey,
        FilmographyQuery query,
        bool filterVisibleItems)
    {
        command.Parameters.AddWithValue("$generationId", generationId);
        command.Parameters.AddWithValue("$actorKey", actorKey);
        command.Parameters.AddWithValue("$personType", query.PersonType);
        command.Parameters.AddWithValue("$filterLibraries", query.LibraryIds.Count > 0 ? 1 : 0);
        command.Parameters.AddWithValue("$filterVisibleItems", filterVisibleItems ? 1 : 0);
    }

    private static async Task CreateQueryFilterTablesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TEMP TABLE selected_libraries (library_id TEXT NOT NULL PRIMARY KEY);
            CREATE TEMP TABLE visible_source_items (source_item_id TEXT NOT NULL PRIMARY KEY);
            """;
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertGuidFilterAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        IReadOnlyCollection<Guid> values,
        CancellationToken cancellationToken)
    {
        var commandText = tableName switch
        {
            "selected_libraries" when columnName == "library_id" =>
                "INSERT OR IGNORE INTO selected_libraries (library_id) VALUES ($value);",
            "visible_source_items" when columnName == "source_item_id" =>
                "INSERT OR IGNORE INTO visible_source_items (source_item_id) VALUES ($value);",
            _ => throw new ArgumentException("Unsupported temporary filter table.", nameof(tableName))
        };

        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var parameter = command.Parameters.Add("$value", SqliteType.Text);
        foreach (var value in values.Distinct())
        {
            parameter.Value = value.ToString("N", CultureInfo.InvariantCulture);
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ReplaceItemAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long generationId,
        IndexedMediaItem item,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(item.ItemType);
        ArgumentNullException.ThrowIfNull(item.LibraryIds);
        ArgumentNullException.ThrowIfNull(item.Credits);

        var sourceItemId = item.SourceItemId.ToString("N", CultureInfo.InvariantCulture);
        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText =
                "DELETE FROM media_items WHERE generation_id = $generationId AND source_item_id = $sourceItemId;";
            deleteCommand.Parameters.AddWithValue("$generationId", generationId);
            deleteCommand.Parameters.AddWithValue("$sourceItemId", sourceItemId);
            _ = await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        using (var itemCommand = connection.CreateCommand())
        {
            itemCommand.Transaction = transaction;
            itemCommand.CommandText =
                """
                INSERT INTO media_items (
                    generation_id, source_item_id, display_item_id, name, item_type, production_year, date_last_saved_utc)
                VALUES (
                    $generationId, $sourceItemId, $displayItemId, $name, $itemType, $productionYear, $dateLastSavedUtc);
                """;
            itemCommand.Parameters.AddWithValue("$generationId", generationId);
            itemCommand.Parameters.AddWithValue("$sourceItemId", sourceItemId);
            itemCommand.Parameters.AddWithValue("$displayItemId", item.DisplayItemId.ToString("N", CultureInfo.InvariantCulture));
            itemCommand.Parameters.AddWithValue("$name", item.Name);
            itemCommand.Parameters.AddWithValue("$itemType", item.ItemType);
            itemCommand.Parameters.AddWithValue("$productionYear", item.Year is null ? DBNull.Value : item.Year.Value);
            itemCommand.Parameters.AddWithValue("$dateLastSavedUtc", FormatTimestamp(item.DateLastSavedUtc));
            _ = await itemCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var libraryId in item.LibraryIds.Distinct())
        {
            using var libraryCommand = connection.CreateCommand();
            libraryCommand.Transaction = transaction;
            libraryCommand.CommandText =
                """
                INSERT INTO media_item_libraries (generation_id, source_item_id, library_id)
                VALUES ($generationId, $sourceItemId, $libraryId);
                """;
            libraryCommand.Parameters.AddWithValue("$generationId", generationId);
            libraryCommand.Parameters.AddWithValue("$sourceItemId", sourceItemId);
            libraryCommand.Parameters.AddWithValue("$libraryId", libraryId.ToString("N", CultureInfo.InvariantCulture));
            _ = await libraryCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var credit in item.Credits.Distinct())
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(credit.ActorKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(credit.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(credit.PersonType);

            using var creditCommand = connection.CreateCommand();
            creditCommand.Transaction = transaction;
            creditCommand.CommandText =
                """
                INSERT OR REPLACE INTO credits (
                    generation_id, source_item_id, actor_key, person_id, name, role, person_type)
                VALUES (
                    $generationId, $sourceItemId, $actorKey, $personId, $name, $role, $personType);
                """;
            creditCommand.Parameters.AddWithValue("$generationId", generationId);
            creditCommand.Parameters.AddWithValue("$sourceItemId", sourceItemId);
            creditCommand.Parameters.AddWithValue("$actorKey", credit.ActorKey);
            creditCommand.Parameters.AddWithValue(
                "$personId",
                credit.PersonId is null ? DBNull.Value : credit.PersonId.Value.ToString("N", CultureInfo.InvariantCulture));
            creditCommand.Parameters.AddWithValue("$name", credit.Name);
            creditCommand.Parameters.AddWithValue("$role", credit.Role ?? string.Empty);
            creditCommand.Parameters.AddWithValue("$personType", credit.PersonType);
            _ = await creditCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<long?> GetActiveGenerationIdAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT active_generation FROM index_state WHERE singleton_id = 1;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null || result == DBNull.Value
            ? null
            : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            """;
        _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        return connection;
    }
}
