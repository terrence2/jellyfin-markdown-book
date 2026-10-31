using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MarkdownBook.Configuration;

/// <summary>
/// Plugin configuration for the Markdown Books plugin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the web client reader is injected into
    /// the served <c>index.html</c>/<c>config.json</c> via the File Transformation plugin.
    /// </summary>
    public bool EnableInjection { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether raw HTML in Markdown files is rendered
    /// verbatim. Disabled by default, and only safe on single-user/trusted servers.
    /// </summary>
    public bool AllowRawHtml { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether advanced Markdown extensions
    /// (tables, footnotes, task lists, auto-links, ...) are enabled.
    /// </summary>
    public bool EnableAdvancedExtensions { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether debug logging is enabled.
    /// </summary>
    public bool DebugLogging { get; set; } = false;
}