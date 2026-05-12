// BackgroundInferenceWorker.cs
//
// IHostedService adapter that wraps IAIService in the .NET Generic Host
// lifecycle. Intended for server, desktop (Windows/macOS/Linux) and
// Windows Service / systemd unit deployments — NOT for MAUI apps, which
// use the platform-specific service in Circle.AI.Maui instead.
//
// Registration in the host's DI container:
//   services.AddHostedService<BackgroundInferenceWorker>();

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Circle.AI.Hosting;

/// <summary>
/// Wraps a <see cref="IAIService"/> in an <see cref="IHostedService"/> so
/// it participates in the .NET Generic Host lifecycle (dotnet run, Windows
/// Service, systemd unit).
/// </summary>
/// <remarks>
/// Register with:
/// <code>
/// services.AddHostedService&lt;BackgroundInferenceWorker&gt;();
/// </code>
/// The worker honours <see cref="IThermalThrottleService"/> when one is
/// registered in DI; it sets <see cref="IsPaused"/> to <c>true</c> while the
/// device is thermally throttled (<see cref="ThermalState.Serious"/> or
/// <see cref="ThermalState.Critical"/>) and logs a warning.  Callers that
/// drive inference should check <see cref="IsPaused"/> before submitting work.
/// </remarks>
public sealed class BackgroundInferenceWorker : IHostedService, IAsyncDisposable
{
    // ------------------------------------------------------------------
    // Fields
    // ------------------------------------------------------------------

    private readonly IAIService _butler;
    private readonly ILogger<BackgroundInferenceWorker> _logger;
    private readonly IThermalThrottleService? _thermal;

    // True while the device is in Serious / Critical thermal state.
    private volatile bool _paused;

    // Tracks whether StopAsync has already been called to prevent double-stop.
    private int _stopped; // 0 = running, 1 = stopped

    // ------------------------------------------------------------------
    // Constructor
    // ------------------------------------------------------------------

    /// <summary>
    /// Initialises the worker.
    /// </summary>
    /// <param name="butler">
    /// The butler service to start and stop with the host.
    /// </param>
    /// <param name="logger">Logger for lifecycle and thermal events.</param>
    /// <param name="thermal">
    /// Optional thermal throttle service. When <c>null</c> thermal monitoring
    /// is skipped and <see cref="IsPaused"/> is always <c>false</c>.
    /// </param>
    public BackgroundInferenceWorker(
        IAIService butler,
        ILogger<BackgroundInferenceWorker> logger,
        IThermalThrottleService? thermal = null)
    {
        _butler  = butler  ?? throw new ArgumentNullException(nameof(butler));
        _logger  = logger  ?? throw new ArgumentNullException(nameof(logger));
        _thermal = thermal;
    }

    // ------------------------------------------------------------------
    // Properties
    // ------------------------------------------------------------------

    /// <summary>
    /// <c>true</c> while the device is in a thermally-throttled state
    /// (<see cref="ThermalState.Serious"/> or <see cref="ThermalState.Critical"/>).
    /// Callers that queue inference work should check this before submitting.
    /// </summary>
    public bool IsPaused => _paused;

    // ------------------------------------------------------------------
    // IHostedService
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// Starts the butler service (model load + optional warm-up) and, if a
    /// <see cref="IThermalThrottleService"/> is available, begins monitoring
    /// device temperature.
    /// </remarks>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("BackgroundInferenceWorker starting.");

        if (_thermal is not null)
        {
            _thermal.StateChanged += OnThermalStateChanged;
            _thermal.StartMonitoring(cancellationToken);
            _logger.LogDebug("Thermal monitoring started (initial state: {State}).",
                _thermal.CurrentState);
        }

        await _butler.StartAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("BackgroundInferenceWorker started; butler is ready.");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Stops the butler service and thermal monitoring in order.
    /// Safe to call multiple times — subsequent calls are no-ops.
    /// </remarks>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _stopped, 1, 0) != 0)
            return;

        _logger.LogInformation("BackgroundInferenceWorker stopping.");

        if (_thermal is not null)
        {
            _thermal.StateChanged -= OnThermalStateChanged;
            _thermal.StopMonitoring();
        }

        await _butler.StopAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("BackgroundInferenceWorker stopped.");
    }

    // ------------------------------------------------------------------
    // IAsyncDisposable
    // ------------------------------------------------------------------

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // StopAsync guards against double-stop internally.
        await StopAsync(CancellationToken.None).ConfigureAwait(false);

        // Dispose the butler service which implements IAsyncDisposable.
        await _butler.DisposeAsync().ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // Thermal event handler
    // ------------------------------------------------------------------

    private void OnThermalStateChanged(object? sender, ThermalState newState)
    {
        bool shouldPause = newState >= ThermalState.Serious;

        if (shouldPause && !_paused)
        {
            _paused = true;
            _logger.LogWarning(
                "Thermal state elevated to {State}. Inference is paused. " +
                "Check IsPaused before submitting new inference work.",
                newState);
        }
        else if (!shouldPause && _paused)
        {
            _paused = false;
            _logger.LogInformation(
                "Thermal state returned to {State}. Inference is resumed.",
                newState);
        }
    }
}
