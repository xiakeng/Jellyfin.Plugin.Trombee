using Jellyfin.Plugin.Trombee.Persistence;
using Jellyfin.Plugin.Trombee.Services;
using Jellyfin.Plugin.Trombee.Tasks;
using MediaBrowser.Model.Tasks;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Trombee.Tests.Tasks;

public sealed class ActorsIndexScheduledTaskTests
{
    [Fact]
    public void IncrementalTaskDefaultsToDailyThreeAm()
    {
        var task = new UpdateActorsIndexTask(Mock.Of<IActorsIndexMaintenanceService>());

        var trigger = Assert.Single(task.GetDefaultTriggers());

        Assert.Equal(TaskTriggerInfoType.DailyTrigger, trigger.Type);
        Assert.Equal(TimeSpan.FromHours(3).Ticks, trigger.TimeOfDayTicks);
    }

    [Fact]
    public void FullRebuildTaskHasNoDefaultTriggers()
    {
        var task = new RebuildActorsIndexTask(Mock.Of<IActorsIndexMaintenanceService>());

        Assert.Empty(task.GetDefaultTriggers());
    }

    [Fact]
    public async Task BootstrapTaskIsHiddenAndDefaultsToStartup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            var task = new ActorsIndexBootstrapTask(store, Mock.Of<ITaskManager>());

            var configurableTask = Assert.IsAssignableFrom<IConfigurableScheduledTask>(task);
            Assert.True(configurableTask.IsHidden);
            Assert.True(configurableTask.IsEnabled);
            Assert.True(configurableTask.IsLogged);
            var trigger = Assert.Single(task.GetDefaultTriggers());
            Assert.Equal(TaskTriggerInfoType.StartupTrigger, trigger.Type);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BootstrapTaskQueuesRebuildWhenNoActiveGenerationExists()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            _ = await store.InitializeAsync(CancellationToken.None);
            var taskManager = new Mock<ITaskManager>();
            var task = new ActorsIndexBootstrapTask(store, taskManager.Object);

            await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

            taskManager.Verify(
                manager => manager.QueueIfNotRunning<RebuildActorsIndexTask>(),
                Times.Once);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BootstrapTaskDoesNotQueueRebuildWhenActiveGenerationExists()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            _ = await store.InitializeAsync(CancellationToken.None);
            var generationId = await store.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            await store.ActivateRebuildAsync(generationId, DateTimeOffset.UtcNow, CancellationToken.None);
            var taskManager = new Mock<ITaskManager>();
            var task = new ActorsIndexBootstrapTask(store, taskManager.Object);

            await task.ExecuteAsync(new Progress<double>(), CancellationToken.None);

            taskManager.Verify(
                manager => manager.QueueIfNotRunning<RebuildActorsIndexTask>(),
                Times.Never);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
