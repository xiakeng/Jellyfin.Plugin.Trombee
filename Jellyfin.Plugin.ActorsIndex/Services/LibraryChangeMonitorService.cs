using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trombee.Persistence;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Trombee.Services;

/// <summary>
/// Debounces Jellyfin library events and applies them to the active actors index.
/// </summary>
public sealed class LibraryChangeMonitorService : BackgroundService
{
    private static readonly TimeSpan _debounceDelay = TimeSpan.FromSeconds(2);

    private static readonly Action<ILogger, Exception?> _logBatchFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(3, "ActorsIndexLiveBatchFailed"),
        "Failed to process a Trombee live library-change batch");

    private static readonly Action<ILogger, Exception?> _logInitializationFailed = LoggerMessage.Define(
        LogLevel.Error,
        new EventId(8, "ActorsIndexLiveMonitorInitializationFailed"),
        "Trombee live library-change monitoring is disabled because the actors-index database could not be initialized");

    private readonly ILibraryManager _libraryManager;
    private readonly SqliteActorsIndexStore _store;
    private readonly IActorsIndexMaintenanceService _maintenanceService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LibraryChangeMonitorService> _logger;
    private readonly LibraryChangeAccumulator _accumulator = new();
    private readonly Channel<byte> _signals = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });

    private bool _subscribed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryChangeMonitorService"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager.</param>
    /// <param name="store">The durable actors-index store.</param>
    /// <param name="maintenanceService">The maintenance coordinator.</param>
    /// <param name="timeProvider">The debounce time provider.</param>
    /// <param name="logger">The logger.</param>
    public LibraryChangeMonitorService(
        ILibraryManager libraryManager,
        SqliteActorsIndexStore store,
        IActorsIndexMaintenanceService maintenanceService,
        TimeProvider timeProvider,
        ILogger<LibraryChangeMonitorService> logger)
    {
        _libraryManager = libraryManager;
        _store = store;
        _maintenanceService = maintenanceService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ = await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logInitializationFailed(_logger, ex);
            await base.StartAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        Subscribe();
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Unsubscribe();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _signals.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                while (_signals.Reader.TryRead(out _))
                {
                }

                await Task.Delay(_debounceDelay, _timeProvider, stoppingToken).ConfigureAwait(false);
                while (_signals.Reader.TryRead(out _))
                {
                }

                var changes = _accumulator.Drain();
                if (changes.Count == 0 || !IsMonitoringEnabled())
                {
                    continue;
                }

                try
                {
                    await _maintenanceService.ProcessChangesAsync(changes, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logBatchFailed(_logger, ex);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private static bool IsIndexable(BaseItem item)
    {
        return item is Movie or Series or Episode;
    }

    private static bool IsMonitoringEnabled()
    {
        var configuration = Plugin.Instance?.Configuration;
        return configuration is null || (configuration.Enabled && configuration.MonitorLibraryChanges);
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs eventArgs)
    {
        Record(eventArgs.Item, LibraryChangeKind.Added);
    }

    private void OnItemUpdated(object? sender, ItemChangeEventArgs eventArgs)
    {
        Record(eventArgs.Item, LibraryChangeKind.Updated);
    }

    private void OnItemRemoved(object? sender, ItemChangeEventArgs eventArgs)
    {
        Record(eventArgs.Item, LibraryChangeKind.Removed);
    }

    private void Record(BaseItem item, LibraryChangeKind kind)
    {
        if (!IsMonitoringEnabled() || !IsIndexable(item))
        {
            return;
        }

        _accumulator.Record(new LibraryChange(item.Id, kind));
        _ = _signals.Writer.TryWrite(0);
    }

    private void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        _libraryManager.ItemAdded += OnItemAdded;
        _libraryManager.ItemUpdated += OnItemUpdated;
        _libraryManager.ItemRemoved += OnItemRemoved;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
        {
            return;
        }

        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemUpdated -= OnItemUpdated;
        _libraryManager.ItemRemoved -= OnItemRemoved;
        _subscribed = false;
    }
}
