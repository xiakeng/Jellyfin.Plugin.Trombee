using Jellyfin.Plugin.Trombee.Services;
using Xunit;

namespace Jellyfin.Plugin.Trombee.Tests.Services;

public sealed class LibraryChangeAccumulatorTests
{
    [Fact]
    public void RemovalWinsWhenAnItemChangesRepeatedly()
    {
        var accumulator = new LibraryChangeAccumulator();
        var itemId = Guid.NewGuid();

        accumulator.Record(new LibraryChange(itemId, LibraryChangeKind.Added));
        accumulator.Record(new LibraryChange(itemId, LibraryChangeKind.Removed));
        accumulator.Record(new LibraryChange(itemId, LibraryChangeKind.Updated));

        var change = Assert.Single(accumulator.Drain());
        Assert.Equal(itemId, change.SourceItemId);
        Assert.Equal(LibraryChangeKind.Removed, change.Kind);
        Assert.Empty(accumulator.Drain());
    }
}
