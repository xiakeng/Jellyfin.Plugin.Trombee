using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Trombee.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the plugin is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum number of actor appearances required.
    /// </summary>
    public int MinimumAppearances { get; set; } = 1;

    /// <summary>
    /// Gets or sets a value indicating whether to show the role name.
    /// </summary>
    public bool ShowRoleName { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Jellyfin library changes are indexed immediately.
    /// </summary>
    public bool MonitorLibraryChanges { get; set; } = true;
}
