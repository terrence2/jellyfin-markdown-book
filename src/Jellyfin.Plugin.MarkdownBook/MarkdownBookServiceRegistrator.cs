using Jellyfin.Plugin.MarkdownBook.Infrastructure;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.MarkdownBook;

/// <summary>
/// Registers the plugin's services into the host container.
/// </summary>
public class MarkdownBookServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHostedService<StartupService>();
    }
}