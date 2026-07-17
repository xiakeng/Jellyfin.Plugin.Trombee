using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Trombee.Persistence;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Trombee.Tasks;

/// <summary>
/// Queues the initial actors-index rebuild after Jellyfin registers scheduled tasks.
/// </summary>
public sealed class ActorsIndexBootstrapTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly SqliteActorsIndexStore _store;
    private readonly ITaskManager _taskManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorsIndexBootstrapTask"/> class.
    /// </summary>
    /// <param name="store">The durable actors-index store.</param>
    /// <param name="taskManager">The Jellyfin scheduled-task manager.</param>
    public ActorsIndexBootstrapTask(SqliteActorsIndexStore store, ITaskManager taskManager)
    {
        _store = store;
        _taskManager = taskManager;
    }

    /// <inheritdoc />
    public string Name => "Initialize Trombee actor data";

    /// <inheritdoc />
    public string Key => "TrombeeActorsIndexBootstrap";

    /// <inheritdoc />
    public string Description => "Queues an initial actor-data rebuild when no active index exists.";

    /// <inheritdoc />
    public string Category => "Trombee";

    /// <inheritdoc />
    public bool IsHidden => true;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (!await _store.HasActiveGenerationAsync(cancellationToken).ConfigureAwait(false))
        {
            _taskManager.QueueIfNotRunning<RebuildActorsIndexTask>();
        }

        progress.Report(100);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.StartupTrigger
            }
        ];
    }
}
