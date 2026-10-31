using System.IO;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Resolvers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MarkdownBook.Resolvers;

/// <summary>
/// Resolves Markdown (.md) files as <see cref="Book"/> items inside Books libraries,
/// mirroring what the core BookResolver does for epub/pdf/cbz and friends.
/// </summary>
public class MarkdownBookResolver : ItemResolver<Book>
{
    private readonly ILogger<MarkdownBookResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MarkdownBookResolver"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public MarkdownBookResolver(ILogger<MarkdownBookResolver> logger)
    {
        _logger = logger;

        // The resolver is instantiated once at server startup; this line proves the
        // plugin's resolver actually entered the server's resolver list when indexing
        // is suspected to be broken.
        _logger.LogInformation("Markdown Books: resolver registered — .md files in Books libraries are indexed as books");
    }

    /// <inheritdoc />
    // Runs ahead of all first-party resolvers, but only claims files the core BookResolver
    // would ignore, so it cannot steal items from other resolvers.
    public override ResolverPriority Priority => ResolverPriority.Plugin;

    /// <inheritdoc />
    protected override Book? Resolve(ItemResolveArgs args)
    {
        // Mirrors the core BookResolver's guard: only act inside Books collection folders.
        if (args.GetCollectionType() != CollectionType.books
            || args.IsDirectory
            || !Path.GetExtension(args.Path.AsSpan()).Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (Plugin.Instance?.Configuration.DebugLogging == true)
        {
            _logger.LogInformation(
                "Markdown Books: resolved '{Path}' as a Book (directory={IsDirectory})",
                args.Path,
                args.IsDirectory);
        }
        else
        {
            _logger.LogDebug("Markdown Books: resolved '{Path}' as a Book", args.Path);
        }

        return new Book
        {
            Path = args.Path,
            Name = Path.GetFileNameWithoutExtension(args.Path),
            IsInMixedFolder = true,

            // Container is mapped to the client DTO unconditionally (unlike Path), and acts
            // as the client-side discriminator: the web client player claims items whose
            // container is "md" even when the item's path field is withheld from the user.
            Container = "md"
        };
    }
}