using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trombee.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Trombee.Services;

/// <summary>
/// Initializes the actors-index database.
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

    private readonly SqliteActorsIndexStore _store;
    private readonly ILogger<ActorsIndexInitializationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorsIndexInitializationService"/> class.
    /// </summary>
    /// <param name="store">The durable actors-index store.</param>
    /// <param name="logger">The logger.</param>
    public ActorsIndexInitializationService(
        SqliteActorsIndexStore store,
        ILogger<ActorsIndexInitializationService> logger)
    {
        _store = store;
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
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
