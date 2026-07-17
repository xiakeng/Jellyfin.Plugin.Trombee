using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trombee.Persistence;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Trombee.Services;

/// <summary>
/// Coordinates full, incremental, and event-driven actors-index maintenance.
/// </summary>
public sealed class ActorsIndexMaintenanceService : IActorsIndexMaintenanceService, IAsyncDisposable
{
    private const int BatchSize = 200;

    private static readonly Action<ILogger, long, Exception?> _logRebuildStarted = LoggerMessage.Define<long>(
        LogLevel.Information,
        new EventId(1, "ActorsIndexRebuildStarted"),
        "Starting Trombee actors-index generation {GenerationId}");

    private static readonly Action<ILogger, long, Exception?> _logRebuildCompleted = LoggerMessage.Define<long>(
        LogLevel.Information,
        new EventId(2, "ActorsIndexRebuildCompleted"),
        "Activated Trombee actors-index generation {GenerationId}");

    private readonly SqliteActorsIndexStore _store;
    private readonly IActorsIndexSource _source;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ActorsIndexMaintenanceService> _logger;
    private readonly SemaphoreSlim _runGate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorsIndexMaintenanceService"/> class.
    /// </summary>
    /// <param name="store">The durable actors-index store.</param>
    /// <param name="source">The Jellyfin item source.</param>
    /// <param name="timeProvider">The UTC time provider.</param>
    /// <param name="logger">The logger.</param>
    public ActorsIndexMaintenanceService(
        SqliteActorsIndexStore store,
        IActorsIndexSource source,
        TimeProvider timeProvider,
        ILogger<ActorsIndexMaintenanceService> logger)
    {
        _store = store;
        _source = source;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Rebuilds all actor data in an inactive generation and atomically activates it on success.
    /// </summary>
    /// <param name="progress">Optional Jellyfin scheduled-task progress.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task representing the rebuild.</returns>
    public async Task FullRebuildAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        await _runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await FullRebuildCoreAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _runGate.Release();
        }
    }

    /// <summary>
    /// Applies items modified since the prior successful run and reconciles deleted source IDs.
    /// </summary>
    /// <param name="progress">Optional Jellyfin scheduled-task progress.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task representing the update.</returns>
    public async Task IncrementalUpdateAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        await _runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
            var generationId = await _store.GetActiveGenerationIdAsync(cancellationToken).ConfigureAwait(false);
            if (generationId is null)
            {
                await FullRebuildCoreAsync(progress, cancellationToken).ConfigureAwait(false);
                return;
            }

            var runStartedUtc = _timeProvider.GetUtcNow();
            var previousWatermark = await _store.GetIncrementalWatermarkAsync(cancellationToken).ConfigureAwait(false)
                ?? DateTimeOffset.UnixEpoch;
            var modifiedSinceUtc = previousWatermark.AddMinutes(-5);
            var total = await _source.GetIndexableItemCountAsync(modifiedSinceUtc, cancellationToken).ConfigureAwait(false);
            var processed = 0;
            var batch = new List<IndexedMediaItem>(BatchSize);
            await foreach (var item in _source.GetItemsAsync(modifiedSinceUtc, cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                batch.Add(item);
                processed++;
                if (batch.Count >= BatchSize)
                {
                    await FlushBatchAsync(generationId.Value, batch, cancellationToken).ConfigureAwait(false);
                }

                progress?.Report(total == 0 ? 0 : processed * 90D / total);
            }

            await FlushBatchAsync(generationId.Value, batch, cancellationToken).ConfigureAwait(false);
            var currentSourceItemIds = await _source.GetCurrentSourceItemIdsAsync(cancellationToken).ConfigureAwait(false);
            var currentSourceItemIdSet = currentSourceItemIds.ToHashSet();
            var storedSourceItemIds = await _store.GetSourceItemIdsAsync(generationId.Value, cancellationToken).ConfigureAwait(false);
            var deletedSourceItemIds = storedSourceItemIds
                .Where(sourceItemId => !currentSourceItemIdSet.Contains(sourceItemId))
                .ToArray();
            await _store.DeleteItemsAsync(generationId.Value, deletedSourceItemIds, cancellationToken).ConfigureAwait(false);
            await _store.CompleteIncrementalAsync(runStartedUtc, cancellationToken).ConfigureAwait(false);
            progress?.Report(100);
        }
        finally
        {
            _runGate.Release();
        }
    }

    /// <summary>
    /// Applies a batch of live Jellyfin library changes to the active generation.
    /// </summary>
    /// <param name="changes">The normalized source-item changes.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task ProcessChangesAsync(
        IReadOnlyCollection<LibraryChange> changes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0)
        {
            return;
        }

        await _runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
            var generationId = await _store.GetActiveGenerationIdAsync(cancellationToken).ConfigureAwait(false);
            if (generationId is null)
            {
                return;
            }

            var normalizedChanges = new Dictionary<Guid, LibraryChangeKind>();
            foreach (var change in changes)
            {
                if (change.SourceItemId == Guid.Empty)
                {
                    continue;
                }

                if (!normalizedChanges.TryGetValue(change.SourceItemId, out var existingKind)
                    || change.Kind == LibraryChangeKind.Removed
                    || existingKind != LibraryChangeKind.Removed)
                {
                    normalizedChanges[change.SourceItemId] = change.Kind;
                }
            }

            var upsertBatch = new List<IndexedMediaItem>(BatchSize);
            var deleteIds = new List<Guid>();
            foreach (var change in normalizedChanges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (change.Value == LibraryChangeKind.Removed)
                {
                    deleteIds.Add(change.Key);
                    continue;
                }

                var item = await _source.GetItemAsync(change.Key, cancellationToken).ConfigureAwait(false);
                if (item is null)
                {
                    deleteIds.Add(change.Key);
                    continue;
                }

                upsertBatch.Add(item);
                if (upsertBatch.Count >= BatchSize)
                {
                    await FlushBatchAsync(generationId.Value, upsertBatch, cancellationToken).ConfigureAwait(false);
                }
            }

            await FlushBatchAsync(generationId.Value, upsertBatch, cancellationToken).ConfigureAwait(false);
            await _store.DeleteItemsAsync(generationId.Value, deleteIds, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _runGate.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _runGate.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private async Task FullRebuildCoreAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var startedUtc = _timeProvider.GetUtcNow();
        var generationId = await _store.BeginRebuildAsync(startedUtc, cancellationToken).ConfigureAwait(false);
        _logRebuildStarted(_logger, generationId, null);

        var total = await _source.GetIndexableItemCountAsync(null, cancellationToken).ConfigureAwait(false);
        var processed = 0;
        var batch = new List<IndexedMediaItem>(BatchSize);
        await foreach (var item in _source.GetItemsAsync(null, cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            batch.Add(item);
            processed++;
            if (batch.Count >= BatchSize)
            {
                await FlushBatchAsync(generationId, batch, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(total == 0 ? 0 : processed * 100D / total);
        }

        await FlushBatchAsync(generationId, batch, cancellationToken).ConfigureAwait(false);
        var completedUtc = _timeProvider.GetUtcNow();
        await _store.ActivateRebuildAsync(
            generationId,
            completedUtc,
            startedUtc,
            cancellationToken).ConfigureAwait(false);
        progress?.Report(100);
        _logRebuildCompleted(_logger, generationId, null);
    }

    private async Task FlushBatchAsync(
        long generationId,
        List<IndexedMediaItem> batch,
        CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        await _store.ReplaceItemsAsync(generationId, batch, cancellationToken).ConfigureAwait(false);
        batch.Clear();
    }
}
