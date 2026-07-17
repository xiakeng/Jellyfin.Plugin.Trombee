using Xunit;

namespace Jellyfin.Plugin.Trombee.Tests.Configuration;

public sealed class FrontendContractTests
{
    [Fact]
    public void BrowsePageUsesServerPagingAndOnDemandFilmography()
    {
        var html = File.ReadAllText(GetConfigurationFilePath("actorsBrowse.html"));

        Assert.Contains("startIndex", html, StringComparison.Ordinal);
        Assert.Contains("limit", html, StringComparison.Ordinal);
        Assert.Contains("Trombee/actors/", html, StringComparison.Ordinal);
        Assert.DoesNotContain("filteredActors", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigPageLoadsAndSavesLibraryChangeMonitoring()
    {
        var html = File.ReadAllText(GetConfigurationFilePath("configPage.html"));

        Assert.Contains("id=\"MonitorLibraryChanges\"", html, StringComparison.Ordinal);
        Assert.Contains("config.MonitorLibraryChanges =", html, StringComparison.Ordinal);
        Assert.Contains("config.MonitorLibraryChanges", html, StringComparison.Ordinal);
        Assert.Contains("startIndex: 0", html, StringComparison.Ordinal);
        Assert.Contains("limit: 60", html, StringComparison.Ordinal);
        Assert.DoesNotContain("actor.items", html, StringComparison.Ordinal);
    }

    private static string GetConfigurationFilePath(string fileName)
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "Jellyfin.Plugin.ActorsIndex",
            "Configuration",
            fileName));
    }
}
