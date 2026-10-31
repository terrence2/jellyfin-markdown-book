namespace Jellyfin.Plugin.MarkdownBook.Api;

/// <summary>
/// The claim types the Jellyfin authentication handler attaches to authenticated
/// principals. Mirrors <c>Jellyfin.Api.Constants.InternalClaimTypes</c> (the host
/// assembly is not referenceable by plugins), restricted to the claims we read.
/// </summary>
public static class JellyfinClaimTypes
{
    /// <summary>The authenticated user id.</summary>
    public const string UserId = "Jellyfin-UserId";

    /// <summary>Whether the token is a server-wide API key.</summary>
    public const string IsApiKey = "Jellyfin-IsApiKey";
}