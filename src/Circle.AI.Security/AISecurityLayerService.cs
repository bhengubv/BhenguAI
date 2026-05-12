namespace Circle.AI.Security;

// ─────────────────────────────────────────────────────────────────────────────
// Transport-agnostic AI Security Layer — full implementation of IPeerSecurityLayer.
//
// Lifecycle:
//   StartAsync  → launches the background trust-recovery loop.
//   (running)   → security events arrive via HandlePeerEvent(PeerSecurityEvent).
//                 Each event degrades the peer's trust score; threshold evaluation
//                 decides which PeerDirective (if any) to issue.
//   StopAsync   → cancels the recovery loop, cleans up.
//
// Any transport (Aether, WiFi, BLE, NearLink, HTTP, …) calls HandlePeerEvent
// after translating its own event type to PeerSecurityEvent.  The bridge lives
// in Circle.AI.Security.Aether (or the equivalent per-transport package).
//
// Directives issued (most-severe wins per event):
//   QuarantineNode     trust ≤ QuarantineThreshold
//   AvoidNode          trust ≤ AvoidNodeThreshold
//   ElevateMonitoring  trust ≤ ElevateMonitoringThreshold
//   ReleaseNode        not issued automatically — requires explicit operator action
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Transport-agnostic AI Security Layer. Degrades per-peer trust scores via
/// <see cref="ThreatDetector"/> and issues <see cref="PeerDirective"/> recommendations
/// to all registered <see cref="IPeerDirectiveConsumer"/> subscribers.
/// </summary>
public sealed class SecurityLayerService : IPeerSecurityLayer
{
    private readonly NodeTrustRegistry _registry;
    private readonly SecurityOptions   _options;
    private readonly DirectivePublisher _publisher;

    private CancellationTokenSource? _cts;
    private Task?                    _recoveryLoop;
    private volatile bool            _active;

    public SecurityLayerService(
        NodeTrustRegistry  registry,
        SecurityOptions    options,
        DirectivePublisher publisher)
    {
        _registry  = registry;
        _options   = options;
        _publisher = publisher;
    }

    // ─── IPeerSecurityLayer ───────────────────────────────────────────────────

    /// <inheritdoc />
    public Task StartAsync(CancellationToken ct = default)
    {
        if (_active) return Task.CompletedTask;
        _cts          = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _recoveryLoop = RunRecoveryLoopAsync(_cts.Token);
        _active       = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken ct = default)
    {
        _active = false;

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
    /// <remarks>
    /// Call this from any transport adapter after translating its native event
    /// type to <see cref="PeerSecurityEvent"/>. Thread-safe.
    /// </remarks>
    public void HandlePeerEvent(PeerSecurityEvent e)
    {
        var degradation = ThreatDetector.ComputeDegradation(e);
        if (degradation <= 0) return;   // PeerThreatLevel.None — no trust impact

        var (previous, current) = _registry.ApplyDegradation(e, degradation);
        EvaluateThresholds(e.NodeId, previous, current, e.Description);
    }

    /// <summary>
    /// Notify the security layer that a peer has left.
    /// Trust entry is preserved for historical queries; no directive is issued.
    /// </summary>
    public void HandlePeerLeft(string nodeId)
    {
        // Trust entry retained for forensic queries; no action required on departure.
    }

    /// <inheritdoc />
    public IDisposable SubscribeToDirectives(IPeerDirectiveConsumer consumer)
        => _publisher.Subscribe(consumer);

    /// <inheritdoc />
    public Task<PeerSecurityPosture> GetPostureAsync(CancellationToken ct = default)
    {
        var nodeIds     = _registry.AllNodeIds.ToList();
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

        return Task.FromResult(new PeerSecurityPosture(
            overallThreat,
            quarantined,
            monitored,
            _active,
            DateTimeOffset.UtcNow));
    }

    // ─── Threshold evaluation ─────────────────────────────────────────────────

    private void EvaluateThresholds(
        string nodeId, double previous, double current, string reason)
    {
        // Evaluate from most-severe to least; issue at most one directive per event.

        if (previous > _options.QuarantineThreshold
         && current  <= _options.QuarantineThreshold)
        {
            IssueDirective(PeerDirectiveKind.QuarantineNode, nodeId,
                current, reason, PeerThreatLevel.Critical);
            return;
        }

        if (previous > _options.AvoidNodeThreshold
         && current  <= _options.AvoidNodeThreshold)
        {
            IssueDirective(PeerDirectiveKind.AvoidNode, nodeId,
                current, reason, PeerThreatLevel.High);
            return;
        }

        if (previous > _options.ElevateMonitoringThreshold
         && current  <= _options.ElevateMonitoringThreshold)
        {
            IssueDirective(PeerDirectiveKind.ElevateMonitoring, nodeId,
                current, reason, PeerThreatLevel.Medium);
        }
    }

    private void IssueDirective(
        PeerDirectiveKind kind, string nodeId, double trustScore,
        string reason, PeerThreatLevel threatLevel)
    {
        _publisher.Publish(new PeerDirective(
            kind,
            TargetNodeId: nodeId,
            TrustScore:   trustScore,
            ThreatLevel:  threatLevel,
            Reason:       reason,
            Duration:     null,               // permanent until ReleaseNode
            IssuedAt:     DateTimeOffset.UtcNow));
    }

    // ─── Background recovery loop ─────────────────────────────────────────────

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

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static PeerThreatLevel ScoreToThreatLevel(double score) => score switch
    {
        <= 0.25 => PeerThreatLevel.Critical,
        <= 0.50 => PeerThreatLevel.High,
        <= 0.75 => PeerThreatLevel.Medium,
        <= 0.90 => PeerThreatLevel.Low,
        _       => PeerThreatLevel.None,
    };
}
