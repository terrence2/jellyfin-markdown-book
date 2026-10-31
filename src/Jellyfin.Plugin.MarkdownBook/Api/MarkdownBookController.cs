using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Markdig;
using Markdig.Extensions.AutoLinks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using IOFile = System.IO.File;
using IOPath = System.IO.Path;

namespace Jellyfin.Plugin.MarkdownBook.Api;

/// <summary>
/// Serves rendered Markdown books and the client-side player script.
/// </summary>
[ApiController]
[Route("[controller]")]
public class MarkdownBookController : ControllerBase
{
    private const string PlayerScriptResourceName = "Jellyfin.Plugin.MarkdownBook.Web.markdown-player.js";

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;

    /// <summary>
    /// The compiled Markdown pipeline, rebuilt when the relevant configuration changes.
    /// </summary>
    private (string? Signature, MarkdownPipeline Pipeline)? _pipelineState;
    private readonly object _pipelineLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MarkdownBookController"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    public MarkdownBookController(ILibraryManager libraryManager, IUserManager userManager)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
    }

    /// <summary>
    /// GET /MarkdownBook/{itemId}/render — returns the item's Markdown file rendered as HTML.
    /// </summary>
    /// <param name="itemId">The id of the Markdown book item.</param>
    /// <returns>The rendered HTML document.</returns>
    [HttpGet("{itemId:guid}/render")]
    [Authorize]
    [Produces("text/html")]
    public IActionResult GetRenderedBook([FromRoute, Required] Guid itemId)
    {
        // API keys authenticate as server administration: no user scoping.
        Book? book;
        if (string.Equals(HttpContext.User.FindFirstValue(JellyfinClaimTypes.IsApiKey), "true", StringComparison.OrdinalIgnoreCase))
        {
            book = _libraryManager.GetItemById<Book>(itemId);
        }
        else
        {
            User? user = null;
            if (Guid.TryParse(HttpContext.User.FindFirstValue(JellyfinClaimTypes.UserId), out var userId))
            {
                user = _userManager.GetUserById(userId);
            }

            if (user is null)
            {
                return Unauthorized();
            }

            // Returns null when the item does not exist or the user cannot access it.
            book = _libraryManager.GetItemById<Book>(itemId, user);
        }

        var path = book?.Path;
        if (path is null
            || !IOPath.GetExtension(path.AsSpan()).Equals(".md", StringComparison.OrdinalIgnoreCase)
            || !IOFile.Exists(path))
        {
            return NotFound();
        }

        var lastWriteUtc = IOFile.GetLastWriteTimeUtc(path);
        var length = new System.IO.FileInfo(path).Length;

        var etag = $"\"{lastWriteUtc.Ticks:x}-{length:x}\"";
        if (Request.Headers.IfNoneMatch.ToString().Contains(etag, StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        string markdown;
        try
        {
            markdown = IOFile.ReadAllText(path);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, $"Unable to read Markdown file: {ex.Message}");
        }

        var html = Markdown.ToHtml(markdown, GetPipeline());

        Response.Headers.ETag = etag;
        return File(
            Encoding.UTF8.GetBytes(BuildDocument(book?.Name, html)),
            "text/html; charset=utf-8");
    }

    /// <summary>
    /// GET /MarkdownBook/web/markdown-player.js — serves the client-side player script.
    /// Deliberately unauthenticated: the script is referenced from index.html, which is
    /// served on the login screen too, and contains no secrets.
    /// </summary>
    /// <returns>The player script.</returns>
    [HttpGet("web/markdown-player.js")]
    [Produces("application/x-javascript")]
    public IActionResult GetPlayerScript()
    {
        var stream = typeof(MarkdownBookController).Assembly.GetManifestResourceStream(PlayerScriptResourceName);
        if (stream is null)
        {
            return NotFound();
        }

        return File(stream, "application/x-javascript");
    }

    private static string BuildDocument(string? title, string bodyHtml)
    {
        var safeTitle = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(title) ? "Markdown Book" : title);
        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>{{safeTitle}}</title>
            </head>
            <body class="markdownReader">
                <article id="markdownReaderContent">{{bodyHtml}}</article>
            </body>
            </html>
            """;
    }

    private MarkdownPipeline GetPipeline()
    {
        var config = Plugin.Instance?.Configuration;
        var signature = config is null ? "defaults" : $"{config.AllowRawHtml}|{config.EnableAdvancedExtensions}";

        lock (_pipelineLock)
        {
            if (_pipelineState is null || _pipelineState.Value.Signature != signature)
            {
                var builder = new MarkdownPipelineBuilder();

                if (config?.EnableAdvancedExtensions != false)
                {
                    builder = builder.UseAdvancedExtensions();
                }

                builder = builder.UseAutoLinks();

                // Default hardening: strip raw HTML from library-owned Markdown. Opt in via
                // config on single-user/trusted servers only.
                if (config?.AllowRawHtml != true)
                {
                    builder = builder.DisableHtml();
                }

                _pipelineState = (signature, builder.Build());
            }

            return _pipelineState.Value.Pipeline;
        }
    }
}