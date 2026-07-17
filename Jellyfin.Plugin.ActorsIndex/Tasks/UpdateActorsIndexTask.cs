using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trombee.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Trombee.Tasks;

/// <summary>
/// Incrementally maintains actor data from Jellyfin item changes.
/// </summary>
public sealed class UpdateActorsIndexTask : IScheduledTask
{
    private readonly IActorsIndexMaintenanceService _maintenanceService;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateActorsIndexTask"/> class.
    /// </summary>
    /// <param name="maintenanceService">The actors-index maintenance service.</param>
    public UpdateActorsIndexTask(IActorsIndexMaintenanceService maintenanceService)
    {
        _maintenanceService = maintenanceService;
    }

    /// <inheritdoc />
    public string Name => "Update Trombee actor data";

    /// <inheritdoc />
    public string Key => "TrombeeUpdateActorsIndex";

    /// <inheritdoc />
    public string Description => "Incrementally updates actor data and reconciles removed Jellyfin items.";

    /// <inheritdoc />
    public string Category => "Trombee";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        return _maintenanceService.IncrementalUpdateAsync(progress, cancellationToken);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
            }
        ];
    }
}
