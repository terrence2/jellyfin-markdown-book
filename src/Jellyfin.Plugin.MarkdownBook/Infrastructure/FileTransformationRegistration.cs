using System.Reflection;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.MarkdownBook.Infrastructure;

/// <summary>
/// Registers web file transformations with the community File Transformation plugin
/// via reflection. A compile-time reference is deliberately impossible: File
/// Transformation lives in a separate, non-collectible AssemblyLoadContext, so its
/// types cannot be shared across the boundary.
/// </summary>
public static class FileTransformationRegistration
{
    private const string FileTransformationAssemblyFragment = "Jellyfin.Plugin.FileTransformation";
    private const string PluginInterfaceTypeName = "Jellyfin.Plugin.FileTransformation.PluginInterface";
    private const string RegisterTransformationMethodName = "RegisterTransformation";

    /// <summary>
    /// The registration id for the index.html transform (stable across restarts so
    /// File Transformation deduplicates instead of stacking transforms).
    /// </summary>
    private static readonly Guid IndexHtmlRegistrationId = new("e6b0d5a2-4d76-4b47-9d4a-9c2b6a52f7d1");

    /// <summary>
    /// The registration id for the config.json transform.
    /// </summary>
    private static readonly Guid ConfigJsonRegistrationId = new("9c4f7e21-83a5-4a1e-9d3e-5f8b2c7a4e60");

    /// <summary>
    /// A (file key, callback method name) pair per registered transformation. The keys
    /// must be the exact relative paths File Transformation matches on; the callback
    /// names must exist as static methods on <see cref="TransformationCallbacks"/>.
    /// </summary>
    private static readonly (string FileKey, string Callback)[] Transformations =
    [
        ("index.html", nameof(TransformationCallbacks.TransformIndexHtml)),
        ("config.json", nameof(TransformationCallbacks.TransformConfigJson))
    ];

    /// <summary>
    /// Registers all transformations. Safe to call repeatedly (File Transformation
    /// deduplicates by id per file).
    /// </summary>
    /// <returns>True when every transformation was registered.</returns>
    public static bool TryRegisterAll()
    {
        var interfaceMethod = FindRegisterTransformationMethod();
        if (interfaceMethod is null)
        {
            LastFailure = "the File Transformation plugin interface was not found";
            return false;
        }

        try
        {
            foreach (var (fileKey, callback) in Transformations)
            {
                var payload = new JObject(
                    new JProperty("id", fileKey == "index.html" ? IndexHtmlRegistrationId : ConfigJsonRegistrationId),
                    new JProperty("fileNamePattern", fileKey),
                    // Exact assembly FullName required by File Transformation's matching;
                    // computed at runtime, never hardcoded.
                    new JProperty("callbackAssembly", typeof(FileTransformationRegistration).Assembly.FullName),
                    new JProperty("callbackClass", typeof(TransformationCallbacks).FullName),
                    new JProperty("callbackMethod", callback));

                interfaceMethod.Invoke(null, [payload]);
            }
        }
        catch (Exception ex)
        {
            // Callers log the retry/give-up outcome; failure here means "not registered yet".
            LastFailure = ex.InnerException?.Message ?? ex.Message;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Gets the reason the last <see cref="TryRegisterAll"/> attempt failed, for diagnostics.
    /// </summary>
    public static string? LastFailure { get; private set; }

    private static MethodInfo? FindRegisterTransformationMethod()
    {
        // File Transformation runs in its own AssemblyLoadContext (its assembly is not
        // collectible precisely so plugins can find it via reflection).
        var assembly = AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .FirstOrDefault(candidate => candidate.FullName?.Contains(
                FileTransformationAssemblyFragment, StringComparison.OrdinalIgnoreCase) == true);

        if (assembly is null)
        {
            return null;
        }

        var interfaceType = assembly.GetType(PluginInterfaceTypeName, throwOnError: false);
        return interfaceType?.GetMethod(RegisterTransformationMethodName);
    }
}