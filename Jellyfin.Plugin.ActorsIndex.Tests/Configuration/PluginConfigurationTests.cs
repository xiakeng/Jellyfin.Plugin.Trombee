using Jellyfin.Plugin.Trombee.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Trombee.Tests.Configuration;

public sealed class PluginConfigurationTests
{
    [Fact]
    public void LibraryMonitoringDefaultsToEnabled()
    {
        Assert.True(new PluginConfiguration().MonitorLibraryChanges);
    }
}
