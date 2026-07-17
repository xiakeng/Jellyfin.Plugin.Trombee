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
}
