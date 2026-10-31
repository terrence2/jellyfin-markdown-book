using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MarkdownBook.Infrastructure;

/// <summary>
/// Registers the web client injections with the File Transformation plugin shortly
/// after server startup, in the background so server start time is never affected.
/// </summary>
public class StartupService : IHostedService
{
    private const int MaxAttempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    private readonly ILogger<StartupService> _logger;
    private Task? _registrationTask;
    private CancellationTokenSource? _cancellationTokenSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="StartupService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public StartupService(ILogger<StartupService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance?.Configuration.EnableInjection != true
            || Plugin.Instance is null)
        {
            _logger.LogDebug("Markdown Books web client injection is disabled");
            return Task.CompletedTask;
        }

        // A hosted service's StartAsync runs after all plugin assemblies are loaded, but
        // File Transformation's plugin instance may become visible to AssemblyLoadContext
        // reflection slightly later — register on a background short-retry loop instead of
        // blocking server startup.
        _cancellationTokenSource = new CancellationTokenSource();
        _registrationTask = RegisterWithRetryAsync(_cancellationTokenSource.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource?.Cancel();
        if (_registrationTask is not null)
        {
            await _registrationTask.ConfigureAwait(false);
        }
    }

    private async Task RegisterWithRetryAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (FileTransformationRegistration.TryRegisterAll())
            {
                _logger.LogInformation(
                    "Markdown Books: web client reader injected via the File Transformation plugin");
                return;
            }

            _logger.LogDebug(
                "Markdown Books: File Transformation registration attempt {Attempt}/{MaxAttempts} failed: {Reason}",
                attempt,
                MaxAttempts,
                FileTransformationRegistration.LastFailure ?? "unknown reason");

            if (attempt < MaxAttempts)
            {
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        // Graceful degradation is the contract: everything except the in-browser reader
        // keeps working without File Transformation.
        _logger.LogWarning(
            "Markdown Books: web client injection failed ({Reason}) — .md books resolve and render " +
            "via the API, but no reader is injected into the web client. Install the 'File " +
            "Transformation' plugin (or disable injection in the Markdown Books settings) to " +
            "remove this message.",
            FileTransformationRegistration.LastFailure ?? "the File Transformation plugin was not found");
    }
}