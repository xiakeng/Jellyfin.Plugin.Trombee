using System;
using System.IO;
using Jellyfin.Plugin.Trombee.Persistence;
using Jellyfin.Plugin.Trombee.Services;
using Jellyfin.Plugin.Trombee.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Trombee;

/// <summary>
/// Registers plugin services.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton(serviceProvider =>
        {
            var applicationPaths = serviceProvider.GetRequiredService<IApplicationPaths>();
            return new SqliteActorsIndexStore(
                Path.Combine(applicationPaths.DataPath, "trombee", "actors-index.db"));
        });
        serviceCollection.AddSingleton<IActorsIndexSource, JellyfinActorsIndexSource>();
        serviceCollection.AddSingleton<IActorsIndexMaintenanceService, ActorsIndexMaintenanceService>();
        serviceCollection.AddSingleton<IScheduledTask, ActorsIndexBootstrapTask>();
        serviceCollection.AddSingleton<IScheduledTask, UpdateActorsIndexTask>();
        serviceCollection.AddSingleton<IScheduledTask, RebuildActorsIndexTask>();
        serviceCollection.AddSingleton<ActorsIndexService>();
        serviceCollection.AddHostedService<ActorsIndexInitializationService>();
        serviceCollection.AddHostedService<PluginPagesRegistrationService>();
        serviceCollection.AddHostedService<LibraryChangeMonitorService>();

        // Note: the IChannel registration for ActorsIndexChannel has been intentionally
        // removed. Jellyfin's native Channel browsing UI cannot be restyled by the plugin,
        // so Trombee relies solely on its own custom page (actorsBrowse.html) instead.
    }
}
