using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.MarkdownBook.Infrastructure;

/// <summary>
/// Request model the File Transformation plugin passes into our callbacks.
/// Field/property names are matched case-insensitively by Newtonsoft from a
/// <c>{ "contents": ... }</c> payload.
/// </summary>
public sealed class TransformationRequest
{
    /// <summary>
    /// Gets or sets the original file contents.
    /// </summary>
    public string Contents { get; set; } = string.Empty;
}

/// <summary>
/// Static callbacks invoked by the File Transformation plugin on each request for
/// the transformed web files. Returning <c>null</c> makes File Transformation serve
/// the original file, which keeps us safe against future web client layout changes.
/// </summary>
public static partial class TransformationCallbacks
{
    private const string PlayerScriptMarker = "markdown-player.js";

    private const string PlayerSpecMarker = "markdownPlayer/plugin";

    [GeneratedRegex("\"plugins\"\\s*:\\s*\\[", RegexOptions.IgnoreCase)]
    private static partial Regex PluginsArrayStartRegex();

    /// <summary>
    /// Injects the (blocking!) player script tag into the served index.html.
    /// </summary>
    /// <param name="request">The request with the original contents.</param>
    /// <returns>The transformed contents, or null to serve the original file.</returns>
    public static string? TransformIndexHtml(TransformationRequest request)
    {
        var contents = request.Contents;

        if (contents.Contains(PlayerScriptMarker, StringComparison.Ordinal))
        {
            return contents; // already injected (e.g. after a re-registration)
        }

        var headEnd = contents.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headEnd < 0)
        {
            return null;
        }

        var script = "<script src=\"../MarkdownBook/web/markdown-player.js?v="
                     + (Plugin.Instance?.Version?.ToString() ?? "0")
                     + "\"></script>";

        return contents.Insert(headEnd, script);
    }

    /// <summary>
    /// Prepends our window-globals plugin spec to the "plugins" array of the served
    /// web client config.json.
    /// </summary>
    /// <param name="request">The request with the original contents.</param>
    /// <returns>The transformed contents, or null to serve the original file.</returns>
    public static string? TransformConfigJson(TransformationRequest request)
    {
        var contents = request.Contents;

        if (contents.Contains(PlayerSpecMarker, StringComparison.Ordinal))
        {
            return contents; // already injected
        }

        var match = PluginsArrayStartRegex().Match(contents);
        if (!match.Success)
        {
            return null;
        }

        // Registered at the front of the array: plugin priority decides player
        // selection anyway, and load order cannot hurt other players.
        return contents.Insert(match.Index + match.Length, "\"" + PlayerSpecMarker + "\",");
    }
}