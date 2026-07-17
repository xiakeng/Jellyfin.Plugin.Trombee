using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trombee.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Trombee.Tasks;

/// <summary>
/// Rebuilds all actor data when manually started by an administrator.
/// </summary>
public sealed class RebuildActorsIndexTask : IScheduledTask
{
    private readonly IActorsIndexMaintenanceService _maintenanceService;

    /// <summary>
    /// Initializes a new instance of the <see cref="RebuildActorsIndexTask"/> class.
    /// </summary>
    /// <param name="maintenanceService">The actors-index maintenance service.</param>
    public RebuildActorsIndexTask(IActorsIndexMaintenanceService maintenanceService)
    {
        _maintenanceService = maintenanceService;
    }

    /// <inheritdoc />
    public string Name => "Rebuild all Trombee actor data";

    /// <inheritdoc />
    public string Key => "TrombeeRebuildActorsIndex";

    /// <inheritdoc />
    public string Description => "Rebuilds all persisted actor data from the current Jellyfin library.";

    /// <inheritdoc />
    public string Category => "Trombee";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        return _maintenanceService.FullRebuildAsync(progress, cancellationToken);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return [];
    }
}
