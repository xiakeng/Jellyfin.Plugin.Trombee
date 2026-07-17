using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trombee.Persistence;
using Jellyfin.Plugin.Trombee.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Trombee.Services;

/// <summary>
/// Initializes the actors-index database and queues its initial rebuild.
/// </summary>
public sealed class ActorsIndexInitializationService : IHostedService
{
    private static readonly Action<ILogger, SchemaInitializationResult, Exception?> _logSchemaInitialized =
        LoggerMessage.Define<SchemaInitializationResult>(
            LogLevel.Information,
            new EventId(4, "ActorsIndexSchemaInitialized"),
            "Trombee actors-index schema initialization result: {InitializationResult}");

    private static readonly Action<ILogger, Exception?> _logInitializationFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(5, "ActorsIndexSchemaInitializationFailed"),
        "Failed to initialize the Trombee actors-index database");

    private static readonly Action<ILogger, Exception?> _logRebuildQueued = LoggerMessage.Define(
        LogLevel.Information,
        new EventId(6, "ActorsIndexInitialRebuildQueued"),
        "Queued the Trombee full actors-index rebuild after schema creation");

    private static readonly Action<ILogger, Exception?> _logRebuildQueueFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(7, "ActorsIndexInitialRebuildQueueFailed"),
        "Failed to queue the Trombee full actors-index rebuild");

    private readonly SqliteActorsIndexStore _store;
    private readonly ITaskManager _taskManager;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<ActorsIndexInitializationService> _logger;
    private CancellationTokenRegistration _applicationStartedRegistration;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorsIndexInitializationService"/> class.
    /// </summary>
    /// <param name="store">The durable actors-index store.</param>
    /// <param name="taskManager">The Jellyfin scheduled-task manager.</param>
    /// <param name="applicationLifetime">The Jellyfin host lifetime.</param>
    /// <param name="logger">The logger.</param>
    public ActorsIndexInitializationService(
        SqliteActorsIndexStore store,
        ITaskManager taskManager,
        IHostApplicationLifetime applicationLifetime,
        ILogger<ActorsIndexInitializationService> logger)
    {
        _store = store;
        _taskManager = taskManager;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        SchemaInitializationResult result;
        try
        {
            result = await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logInitializationFailed(_logger, ex);
            return;
        }

        _logSchemaInitialized(_logger, result, null);
        if (result is SchemaInitializationResult.Created or SchemaInitializationResult.Recreated)
        {
            _applicationStartedRegistration = _applicationLifetime.ApplicationStarted.Register(QueueRebuild);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _applicationStartedRegistration.DisposeAsync().ConfigureAwait(false);
        _applicationStartedRegistration = default;
    }

    private void QueueRebuild()
    {
        try
        {
            _taskManager.QueueIfNotRunning<RebuildActorsIndexTask>();
            _logRebuildQueued(_logger, null);
        }
        catch (Exception ex)
        {
            _logRebuildQueueFailed(_logger, ex);
        }
    }
}
