using Jellyfin.Plugin.Trombee.Api;
using Jellyfin.Plugin.Trombee.Persistence;
using Jellyfin.Plugin.Trombee.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Text.Json;
using Xunit;

namespace Jellyfin.Plugin.Trombee.Tests.Api;

public sealed class ActorsIndexControllerTests
{
    [Fact]
    public async Task RepositoryEndpointUsesForkIdentity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
            var controller = new ActorsIndexController(
                new ActorsIndexService(store, libraryManager.Object),
                libraryManager.Object,
                Mock.Of<IUserManager>(),
                Mock.Of<IProviderManager>(),
                Mock.Of<IFileSystem>(),
                Mock.Of<IHttpClientFactory>());

            var result = controller.GetRepository();

            var jsonResult = Assert.IsType<JsonResult>(result);
            var json = JsonSerializer.SerializeToElement(jsonResult.Value);
            Assert.Equal("xiakeng", json[0].GetProperty("owner").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CheckUpdateReadsForkManifest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            var handler = new RecordingHttpMessageHandler(
                "[{\"versions\":[{\"version\":\"1.0.0.0\"}]}]");
            var httpClientFactory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
            httpClientFactory.Setup(factory => factory.CreateClient(It.IsAny<string>()))
                .Returns(new HttpClient(handler, disposeHandler: false));
            var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
            var controller = new ActorsIndexController(
                new ActorsIndexService(store, libraryManager.Object),
                libraryManager.Object,
                Mock.Of<IUserManager>(),
                Mock.Of<IProviderManager>(),
                Mock.Of<IFileSystem>(),
                httpClientFactory.Object);

            var result = await controller.CheckUpdate();

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal(
                "https://raw.githubusercontent.com/xiakeng/Jellyfin.Plugin.Trombee/main/manifest.json",
                handler.RequestUri?.AbsoluteUri);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ConfigEndpointIncludesLibraryChangeMonitoringSetting()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
            var controller = new ActorsIndexController(
                new ActorsIndexService(store, libraryManager.Object),
                libraryManager.Object,
                Mock.Of<IUserManager>(),
                Mock.Of<IProviderManager>(),
                Mock.Of<IFileSystem>(),
                Mock.Of<IHttpClientFactory>());

            var result = controller.GetConfig();

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var json = JsonSerializer.SerializeToElement(okResult.Value);
            Assert.True(json.GetProperty("monitorLibraryChanges").GetBoolean());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ActorsEndpointClampsPagingParameters()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            await store.InitializeAsync(CancellationToken.None);
            var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
            var controller = new ActorsIndexController(
                new ActorsIndexService(store, libraryManager.Object),
                libraryManager.Object,
                Mock.Of<IUserManager>(),
                Mock.Of<IProviderManager>(),
                Mock.Of<IFileSystem>(),
                Mock.Of<IHttpClientFactory>())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            var result = await controller.GetActorsIndex(
                -20,
                500,
                null,
                "name",
                "ascending",
                "Actor",
                null,
                CancellationToken.None);

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var page = Assert.IsType<ActorsPage>(okResult.Value);
            Assert.Equal(0, page.StartIndex);
            Assert.Equal(200, page.Limit);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task FilmographyEndpointReturnsPagedPersistedItems()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trombee-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await using var store = new SqliteActorsIndexStore(Path.Combine(directory, "actors-index.db"));
            await store.InitializeAsync(CancellationToken.None);
            var generation = await store.BeginRebuildAsync(DateTimeOffset.UtcNow, CancellationToken.None);
            var itemId = Guid.NewGuid();
            await store.ReplaceItemsAsync(
                generation,
                [new IndexedMediaItem(itemId, itemId, "Persisted Movie", "Movie", 2026, DateTimeOffset.UtcNow, [], [new IndexedCredit("actor-key", null, "Actor", "Lead", "Actor")])],
                CancellationToken.None);
            await store.ActivateRebuildAsync(generation, DateTimeOffset.UtcNow, CancellationToken.None);
            var libraryManager = new Mock<ILibraryManager>(MockBehavior.Strict);
            var controller = new ActorsIndexController(
                new ActorsIndexService(store, libraryManager.Object),
                libraryManager.Object,
                Mock.Of<IUserManager>(),
                Mock.Of<IProviderManager>(),
                Mock.Of<IFileSystem>(),
                Mock.Of<IHttpClientFactory>())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };

            var result = await controller.GetActorItems(
                "actor-key",
                -1,
                500,
                "Actor",
                null,
                CancellationToken.None);

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var page = Assert.IsType<FilmographyPage>(okResult.Value);
            Assert.Equal(0, page.StartIndex);
            Assert.Equal(200, page.Limit);
            Assert.Equal(itemId, Assert.Single(page.Items).ItemId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RecordingHttpMessageHandler(string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody),
            });
        }
    }
}
