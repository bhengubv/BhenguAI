namespace Bhengu.AI.Security;

using Bhengu.AI.Aether;

// ─────────────────────────────────────────────────────────────────────────────
// AI Security Layer — full implementation of IAISecurityLayer.
//
// Lifecycle:
//   StartAsync  → wires this service as an Aether telemetry observer and
//                 launches a background trust-recovery loop.
//   (running)   → security events flow in via TelemetryAdapter; each event
//                 degrades a node's trust score, then threshold evaluation
//                 decides which SecurityDirective (if any) to issue.
//   StopAsync   → unsubscribes from telemetry, cancels the recovery loop.
//
// Directives issued (in order of severity, highest first):
//   QuarantineNode     trust ≤ QuarantineThreshold
//   AvoidNode          trust ≤ AvoidNodeThreshold
//   ElevateMonitoring  trust ≤ ElevateMonitoringThreshold
//   ReleaseNode        (not issued automatically — see SecurityDirectiveKind docs)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Subscribes to <see cref="IAetherTelemetry"/>, degrades per-node trust
/// scores via <see cref="ThreatDetector"/>, and issues
/// <see cref="SecurityDirective"/> recommendations to registered
/// <see cref="ISecurityDirectiveConsumer"/> subscribers.
/// </summary>
public sealed class AISecurityLayerService : IAISecurityLayer
{
    private readonly NodeTrustRegistry _registry;
    private readonly SecurityOptions   _options;
    private readonly DirectivePublisher _publisher;

    private IDisposable?             _telemetrySubscription;
    private CancellationTokenSource? _cts;
    private Task?                    _recoveryLoop;
    private volatile bool            _active;

    public AISecurityLayerService(
        NodeTrustRegistry registry,
        SecurityOptions   options,
        DirectivePublisher publisher)
    {
        _registry  = registry;
        _options   = options;
        _publisher = publisher;
    }

    // ─── IAISecurityLayer ────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task StartAsync(IAetherTelemetry telemetry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        _telemetrySubscription = telemetry.Subscribe(new TelemetryAdapter(this));
        _cts          = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _recoveryLoop = RunRecoveryLoopAsync(_cts.Token);
        _active       = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct = default)
    {
        _active = false;
        _telemetrySubscription?.Dispose();
        _telemetrySubscription = null;

        if (_cts is not null)
        {
            await _cts.CancelAsync();

            if (_recoveryLoop is not null)
            {
                try   { await _recoveryLoop.WaitAsync(ct); }
                catch (OperationCanceledException) { }
            }

            _cts.Dispose();
            _cts = null;
        }
    }

    /// <inheritdoc />
    public IDisposable SubscribeToDirectives(ISecurityDirectiveConsumer consumer)
        => _publisher.Subscribe(consumer);

    /// <inheritdoc />
    public Task<SecurityPosture> GetPostureAsync(CancellationToken ct = default)
    {
        var nodeIds    = _registry.AllNodeIds.ToList();
        var quarantined = nodeIds.Count(
            id => _registry.GetTrustScore(id) <= _options.QuarantineThreshold);
        var monitored   = nodeIds.Count(id =>
        {
            var s = _registry.GetTrustScore(id);
            return s <= _options.ElevateMonitoringThreshold
                && s >  _options.QuarantineThreshold;
        });

        var worstScore    = nodeIds.Count == 0
            ? 1.0
            : nodeIds.Min(id => _registry.GetTrustScore(id));
        var overallThreat = ScoreToThreatLevel(worstScore);

        return Task.FromResult(new SecurityPosture(
            overallThreat,
            quarantined,
            monitored,
            _active,
            DateTimeOffset.UtcNow));
    }

    // ─── Event handling ──────────────────────────────────────────────────────

    internal void HandleSecurityEvent(AetherSecurityEvent e)
    {
        var degradation = ThreatDetector.ComputeDegradation(e);
        if (degradation <= 0) return;                               // None threat level

        var (previous, current) = _registry.ApplyDegradation(e, degradation);
        EvaluateThresholds(e.NodeId, previous, current, e.Description);
    }

    internal void HandleNodeEvent(AetherNodeEvent e)
    {
        // Left events: node is gone — no further action.
        // Trust entry is preserved for historical queries.
    }

    // ─── Threshold evaluation ────────────────────────────────────────────────

    private void EvaluateThresholds(
        string nodeId, double previous, double current, string reason)
    {
        // Evaluate from most-severe to least; issue at most one directive
        // per event (the most severe crossing wins).

        if (previous > _options.QuarantineThreshold
         && current  <= _options.QuarantineThreshold)
        {
            IssueDirective(SecurityDirectiveKind.QuarantineNode, nodeId,
                current, reason, AetherThreatLevel.Critical);
            return;
        }

        if (previous > _options.AvoidNodeThreshold
         && current  <= _options.AvoidNodeThreshold)
        {
            IssueDirective(SecurityDirectiveKind.AvoidNode, nodeId,
                current, reason, AetherThreatLevel.High);
            return;
        }

        if (previous > _options.ElevateMonitoringThreshold
         && current  <= _options.ElevateMonitoringThreshold)
        {
            IssueDirective(SecurityDirectiveKind.ElevateMonitoring, nodeId,
                current, reason, AetherThreatLevel.Medium);
        }
    }

    private void IssueDirective(
        SecurityDirectiveKind kind, string nodeId, double trustScore,
        string reason, AetherThreatLevel threatLevel)
    {
        _publisher.Publish(new SecurityDirective(
            kind,
            TargetNodeId:      nodeId,
            TrustScoreOverride: trustScore,
            ThreatLevel:       threatLevel,
            Reason:            reason,
            Duration:          null,          // permanent until ReleaseNode
            IssuedAt:          DateTimeOffset.UtcNow));
    }

    // ─── Background recovery loop ────────────────────────────────────────────

    private async Task RunRecoveryLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(30);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);
                _registry.ApplyRecovery(interval);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static AetherThreatLevel ScoreToThreatLevel(double score) => score switch
    {
        <= 0.25 => AetherThreatLevel.Critical,
        <= 0.50 => AetherThreatLevel.High,
        <= 0.75 => AetherThreatLevel.Medium,
        <= 0.90 => AetherThreatLevel.Low,
        _       => AetherThreatLevel.None,
    };

    // ─── Telemetry adapter ────────────────────────────────────────────────────

    /// <summary>
    /// Routes IAetherTelemetryObserver callbacks into AISecurityLayerService
    /// without exposing the observer interface on the public type.
    /// </summary>
    private sealed class TelemetryAdapter : IAetherTelemetryObserver
    {
        private readonly AISecurityLayerService _owner;
        internal TelemetryAdapter(AISecurityLayerService owner) => _owner = owner;

        public void OnSecurityEvent(AetherSecurityEvent e)  => _owner.HandleSecurityEvent(e);
        public void OnNodeEvent(AetherNodeEvent e)           => _owner.HandleNodeEvent(e);
        public void OnTransportEvent(AetherTransportEvent e) { }
        public void OnRouteEvent(AetherRouteEvent e)         { }
        public void OnNetworkEvent(AetherNetworkEvent e)     { }
    }
}
