using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bhengu.AI.Aether;
using Bhengu.AI.Security;
using Xunit;

namespace Bhengu.AI.Tests;

// ─── Test helpers ────────────────────────────────────────────────────────────

file static class Make
{
    public static SecurityOptions Opts(
        double elevate   = 0.75,
        double avoid     = 0.50,
        double quarantine = 0.25,
        double initial   = 1.0,
        double recovery  = 0.001) => new()
    {
        ElevateMonitoringThreshold = elevate,
        AvoidNodeThreshold         = avoid,
        QuarantineThreshold        = quarantine,
        InitialTrustScore          = initial,
        RecoveryRatePerSecond      = recovery,
        EventWindow                = TimeSpan.FromMinutes(5),
        MaxEventsPerNode           = 10,
    };

    public static AetherSecurityEvent Sec(
        string nodeId             = "n1",
        AetherSecurityEventKind kind  = AetherSecurityEventKind.RoutingAnomaly,
        AetherThreatLevel level   = AetherThreatLevel.Medium,
        string description        = "test",
        DateTimeOffset? at        = null) =>
        new(nodeId, kind, level, description,
            new Dictionary<string, string>(),
            at ?? DateTimeOffset.UtcNow);

    public static NodeTrustRegistry Registry(SecurityOptions? opts = null) =>
        new(opts ?? Opts());
}

file sealed class CapturingConsumer : ISecurityDirectiveConsumer
{
    public List<SecurityDirective> Received { get; } = new();
    public void OnDirective(SecurityDirective d) => Received.Add(d);
}

/// <summary>Minimal IAetherTelemetry that lets tests fire events manually.</summary>
file sealed class ManualTelemetry : IAetherTelemetry
{
    private readonly List<IAetherTelemetryObserver> _observers = new();

    public IDisposable Subscribe(IAetherTelemetryObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _observers.Add(observer);
        return new Handle(() => _observers.Remove(observer));
    }

    public void FireSecurity(AetherSecurityEvent e)
    {
        foreach (var o in _observers.ToList()) o.OnSecurityEvent(e);
    }

    public void FireNode(AetherNodeEvent e)
    {
        foreach (var o in _observers.ToList()) o.OnNodeEvent(e);
    }

    private sealed class Handle(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}

// ─── ThreatDetector tests ─────────────────────────────────────────────────────

public sealed class ThreatDetectorDegradationTests
{
    [Fact] public void IntrusionCritical_IsHighestDegradation() =>
        Assert.True(
            ThreatDetector.ComputeDegradation(
                Make.Sec(kind: AetherSecurityEventKind.IntrusionSignal,
                         level: AetherThreatLevel.Critical)) >
            ThreatDetector.ComputeDegradation(
                Make.Sec(kind: AetherSecurityEventKind.NodeAuthAttempt,
                         level: AetherThreatLevel.Low)));

    [Fact] public void ThreatNone_AlwaysZero() =>
        Assert.Equal(0.0,
            ThreatDetector.ComputeDegradation(
                Make.Sec(level: AetherThreatLevel.None)));

    [Fact] public void RoutingAnomalyMedium_EqualsBaseWeight()
    {
        var result = ThreatDetector.ComputeDegradation(
            Make.Sec(kind: AetherSecurityEventKind.RoutingAnomaly,
                     level: AetherThreatLevel.Medium));
        Assert.Equal(0.10, result, precision: 5);
    }

    [Fact] public void RoutingAnomalyHigh_IsTwiceBaseWeight()
    {
        var medium = ThreatDetector.ComputeDegradation(
            Make.Sec(kind: AetherSecurityEventKind.RoutingAnomaly,
                     level: AetherThreatLevel.Medium));
        var high = ThreatDetector.ComputeDegradation(
            Make.Sec(kind: AetherSecurityEventKind.RoutingAnomaly,
                     level: AetherThreatLevel.High));
        Assert.Equal(medium * 2, high, precision: 5);
    }

    [Fact] public void AllEventKinds_NonNoneLevel_Positive()
    {
        var kinds = Enum.GetValues<AetherSecurityEventKind>();
        foreach (var kind in kinds)
        {
            var deg = ThreatDetector.ComputeDegradation(
                Make.Sec(kind: kind, level: AetherThreatLevel.Medium));
            Assert.True(deg > 0, $"{kind} should produce positive degradation");
        }
    }

    [Fact] public void PrivilegeAttemptHigh_ExceedsEncryptionHigh()
    {
        var priv = ThreatDetector.ComputeDegradation(
            Make.Sec(kind: AetherSecurityEventKind.PrivilegeAttempt,
                     level: AetherThreatLevel.High));
        var enc = ThreatDetector.ComputeDegradation(
            Make.Sec(kind: AetherSecurityEventKind.EncryptionEvent,
                     level: AetherThreatLevel.High));
        Assert.True(priv > enc);
    }
}

public sealed class ThreatDetectorIndicatorTests
{
    private static AetherSecurityEvent Recent(
        AetherSecurityEventKind kind, AetherThreatLevel level = AetherThreatLevel.Medium) =>
        Make.Sec(kind: kind, level: level, at: DateTimeOffset.UtcNow.AddSeconds(-10));

    [Fact] public void NoEvents_ReturnsEmpty() =>
        Assert.Empty(ThreatDetector.DetectIndicators([], TimeSpan.FromMinutes(5)));

    [Fact] public void ThreeAuthAttempts_FlagsRepeatedAuth()
    {
        var events = Enumerable.Range(0, 3)
            .Select(_ => Recent(AetherSecurityEventKind.NodeAuthAttempt))
            .ToList();
        Assert.Contains("repeated-auth-attempts",
            ThreatDetector.DetectIndicators(events, TimeSpan.FromMinutes(5)));
    }

    [Fact] public void TwoAuthAttempts_NoFlag()
    {
        var events = Enumerable.Range(0, 2)
            .Select(_ => Recent(AetherSecurityEventKind.NodeAuthAttempt))
            .ToList();
        Assert.DoesNotContain("repeated-auth-attempts",
            ThreatDetector.DetectIndicators(events, TimeSpan.FromMinutes(5)));
    }

    [Fact] public void IntrusionSignal_FlagsDetected() =>
        Assert.Contains("intrusion-signal-detected",
            ThreatDetector.DetectIndicators(
                [Recent(AetherSecurityEventKind.IntrusionSignal)],
                TimeSpan.FromMinutes(5)));

    [Fact] public void HighSeverityEvent_FlagsHighSeverity() =>
        Assert.Contains("high-severity-event",
            ThreatDetector.DetectIndicators(
                [Recent(AetherSecurityEventKind.RoutingAnomaly, AetherThreatLevel.High)],
                TimeSpan.FromMinutes(5)));

    [Fact] public void ThreeDistinctKinds_FlagsMultiVector()
    {
        var events = new[]
        {
            Recent(AetherSecurityEventKind.NodeAuthAttempt),
            Recent(AetherSecurityEventKind.RoutingAnomaly),
            Recent(AetherSecurityEventKind.EncryptionEvent),
        };
        Assert.Contains("multi-vector-activity",
            ThreatDetector.DetectIndicators(events, TimeSpan.FromMinutes(5)));
    }

    [Fact] public void PrivilegeAttempt_FlaggedSeparately() =>
        Assert.Contains("privilege-escalation-attempt",
            ThreatDetector.DetectIndicators(
                [Recent(AetherSecurityEventKind.PrivilegeAttempt)],
                TimeSpan.FromMinutes(5)));

    [Fact] public void OldEventsOutsideWindow_Ignored()
    {
        var old = Make.Sec(kind: AetherSecurityEventKind.IntrusionSignal,
                           at: DateTimeOffset.UtcNow.AddHours(-1));
        Assert.DoesNotContain("intrusion-signal-detected",
            ThreatDetector.DetectIndicators([old], TimeSpan.FromMinutes(5)));
    }
}

// ─── NodeTrustRegistry tests ──────────────────────────────────────────────────

public sealed class NodeTrustRegistryTests
{
    [Fact] public void GetOrCreate_NewNode_ReturnsInitialScore()
    {
        var reg = Make.Registry(Make.Opts(initial: 0.9));
        Assert.Equal(0.9, reg.GetOrCreate("x").TrustScore, precision: 5);
    }

    [Fact] public void GetOrCreate_SameNode_ReturnsSameInstance()
    {
        var reg = Make.Registry();
        Assert.Same(reg.GetOrCreate("a"), reg.GetOrCreate("a"));
    }

    [Fact] public void ApplyDegradation_ReducesScore()
    {
        var reg = Make.Registry();
        var (_, current) = reg.ApplyDegradation(Make.Sec("n"), 0.20);
        Assert.Equal(0.80, current, precision: 5);
    }

    [Fact] public void ApplyDegradation_ClampedAtZero()
    {
        var reg = Make.Registry();
        var (_, current) = reg.ApplyDegradation(Make.Sec("n"), 2.0);
        Assert.Equal(0.0, current, precision: 5);
    }

    [Fact] public async Task ApplyDegradation_PublishesUpdateToChannel()
    {
        var reg = Make.Registry();
        reg.ApplyDegradation(Make.Sec("n"), 0.10);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var update = await reg.TrustScoreUpdates.ReadAsync(cts.Token);
        Assert.Equal("n", update.NodeId);
        Assert.Equal(0.90, update.CurrentScore, precision: 5);
    }

    [Fact] public void ApplyDegradation_NoneLevel_ZeroDegradation_NoPublish()
    {
        // Zero degradation → previous == current → no publish
        var reg     = Make.Registry();
        var secEvt  = Make.Sec(level: AetherThreatLevel.None);
        var deg     = ThreatDetector.ComputeDegradation(secEvt);   // 0
        reg.ApplyDegradation(secEvt, deg);

        Assert.False(reg.TrustScoreUpdates.TryRead(out _));
    }

    [Fact] public void ApplyRecovery_IncreasesScore()
    {
        var reg = Make.Registry(Make.Opts(recovery: 0.01, initial: 1.0));
        reg.ApplyDegradation(Make.Sec("n"), 0.50);     // score → 0.50
        reg.ApplyRecovery(TimeSpan.FromSeconds(10));   // +0.10
        Assert.Equal(0.60, reg.GetTrustScore("n"), precision: 5);
    }

    [Fact] public void ApplyRecovery_ClampedAtOne()
    {
        var reg = Make.Registry(Make.Opts(recovery: 100.0, initial: 1.0));
        reg.ApplyDegradation(Make.Sec("n"), 0.10);     // score → 0.90
        reg.ApplyRecovery(TimeSpan.FromSeconds(1));    // massive recovery
        Assert.Equal(1.0, reg.GetTrustScore("n"), precision: 5);
    }

    [Fact] public void ApplyRecovery_ZeroElapsed_NoChange()
    {
        var reg = Make.Registry();
        reg.ApplyDegradation(Make.Sec("n"), 0.30);
        // drain existing update
        reg.TrustScoreUpdates.TryRead(out _);

        reg.ApplyRecovery(TimeSpan.Zero);
        Assert.False(reg.TrustScoreUpdates.TryRead(out _));
    }

    [Fact] public void GetRecentEvents_ReturnsWindowedEvents()
    {
        var reg = Make.Registry(Make.Opts());
        reg.ApplyDegradation(Make.Sec("n", at: DateTimeOffset.UtcNow.AddSeconds(-30)), 0.05);
        Assert.Single(reg.GetRecentEvents("n"));
    }

    [Fact] public void GetRecentEvents_OldEventsExcluded()
    {
        var opts = Make.Opts();
        opts.EventWindow = TimeSpan.FromSeconds(10);
        var reg = new NodeTrustRegistry(opts);
        reg.ApplyDegradation(Make.Sec("n", at: DateTimeOffset.UtcNow.AddMinutes(-5)), 0.05);
        Assert.Empty(reg.GetRecentEvents("n"));
    }

    [Fact] public void GetRecentEvents_UnknownNode_Empty() =>
        Assert.Empty(Make.Registry().GetRecentEvents("nobody"));

    [Fact] public void GetTrustScore_KnownNode_ReturnsCurrentScore()
    {
        var reg = Make.Registry();
        reg.ApplyDegradation(Make.Sec("n"), 0.30);
        Assert.Equal(0.70, reg.GetTrustScore("n"), precision: 5);
    }

    [Fact] public void GetTrustScore_UnknownNode_ReturnsInitial()
    {
        var reg = Make.Registry(Make.Opts(initial: 0.95));
        Assert.Equal(0.95, reg.GetTrustScore("ghost"), precision: 5);
    }

    [Fact] public void MaxEventsPerNode_OldestDropped()
    {
        var opts = Make.Opts();
        opts.MaxEventsPerNode = 3;
        var reg = new NodeTrustRegistry(opts);
        for (var i = 0; i < 5; i++)
            reg.ApplyDegradation(Make.Sec("n"), 0.01);

        // GetRecentEvents only returns within window — but entry count is capped at 3
        var entry = reg.GetOrCreate("n");
        Assert.Equal(3, entry.RecentEvents.Count);
    }
}

// ─── DirectivePublisher tests ─────────────────────────────────────────────────

public sealed class DirectivePublisherTests
{
    private static SecurityDirective ADirective() =>
        new(SecurityDirectiveKind.AvoidNode, "n1", null,
            AetherThreatLevel.High, "test", null, DateTimeOffset.UtcNow);

    [Fact] public void Subscribe_ReturnsNonNullHandle()
    {
        var pub = new DirectivePublisher();
        Assert.NotNull(pub.Subscribe(new CapturingConsumer()));
    }

    [Fact] public void Publish_DeliversToSubscriber()
    {
        var pub      = new DirectivePublisher();
        var consumer = new CapturingConsumer();
        pub.Subscribe(consumer);
        pub.Publish(ADirective());
        Assert.Single(consumer.Received);
    }

    [Fact] public void Publish_NoConsumers_NoException()
    {
        var pub = new DirectivePublisher();
        pub.Publish(ADirective()); // should not throw
    }

    [Fact] public void Publish_MultipleConsumers_AllReceive()
    {
        var pub = new DirectivePublisher();
        var c1  = new CapturingConsumer();
        var c2  = new CapturingConsumer();
        pub.Subscribe(c1);
        pub.Subscribe(c2);
        pub.Publish(ADirective());
        Assert.Single(c1.Received);
        Assert.Single(c2.Received);
    }

    [Fact] public void Dispose_RemovesConsumer()
    {
        var pub      = new DirectivePublisher();
        var consumer = new CapturingConsumer();
        var handle   = pub.Subscribe(consumer);
        handle.Dispose();
        pub.Publish(ADirective());
        Assert.Empty(consumer.Received);
    }

    [Fact] public void Dispose_Twice_DoesNotThrow()
    {
        var pub    = new DirectivePublisher();
        var handle = pub.Subscribe(new CapturingConsumer());
        handle.Dispose();
        handle.Dispose(); // idempotent
    }

    [Fact] public void Subscribe_NullConsumer_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            new DirectivePublisher().Subscribe(null!));

    [Fact] public void SubscriberCount_TracksCorrectly()
    {
        var pub = new DirectivePublisher();
        Assert.Equal(0, pub.SubscriberCount);
        var h = pub.Subscribe(new CapturingConsumer());
        Assert.Equal(1, pub.SubscriberCount);
        h.Dispose();
        Assert.Equal(0, pub.SubscriberCount);
    }
}

// ─── AISecurityLayerService tests ─────────────────────────────────────────────

public sealed class AISecurityLayerServiceTests
{
    private static (AISecurityLayerService svc, NodeTrustRegistry reg, DirectivePublisher pub)
        BuildSvc(SecurityOptions? opts = null)
    {
        var o   = opts ?? Make.Opts();
        var reg = new NodeTrustRegistry(o);
        var pub = new DirectivePublisher();
        var svc = new AISecurityLayerService(reg, o, pub);
        return (svc, reg, pub);
    }

    [Fact] public async Task StartAsync_AcceptsTelemetryWithoutThrowing()
    {
        var (svc, _, _) = BuildSvc();
        await svc.StartAsync(new ManualTelemetry());
        await svc.StopAsync();
    }

    [Fact] public async Task StartAsync_NullTelemetry_Throws()
    {
        var (svc, _, _) = BuildSvc();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => svc.StartAsync(null!));
    }

    [Fact] public async Task SecurityEvent_DegradesTrustRegistry()
    {
        var (svc, reg, _) = BuildSvc();
        var tel = new ManualTelemetry();
        await svc.StartAsync(tel);

        tel.FireSecurity(Make.Sec("node-A",
            kind: AetherSecurityEventKind.RoutingAnomaly,
            level: AetherThreatLevel.Medium));  // −0.10

        Assert.Equal(0.90, reg.GetTrustScore("node-A"), precision: 5);
        await svc.StopAsync();
    }

    [Fact] public async Task SecurityEvent_NoneLevel_NoScoreChange()
    {
        var (svc, reg, _) = BuildSvc();
        var tel = new ManualTelemetry();
        await svc.StartAsync(tel);

        tel.FireSecurity(Make.Sec("n", level: AetherThreatLevel.None));
        Assert.Equal(1.0, reg.GetTrustScore("n"), precision: 5);
        await svc.StopAsync();
    }

    [Fact] public async Task SecurityEvent_CrossesElevateMonitoring_IssuesDirective()
    {
        // Start score at exactly 0.80 so one RoutingAnomaly/Medium (−0.10) crosses 0.75
        var opts     = Make.Opts(elevate: 0.75, avoid: 0.50, quarantine: 0.25, initial: 0.80);
        var (svc, _, pub) = BuildSvc(opts);
        var consumer = new CapturingConsumer();
        pub.Subscribe(consumer);
        var tel = new ManualTelemetry();
        await svc.StartAsync(tel);

        tel.FireSecurity(Make.Sec("n",
            kind: AetherSecurityEventKind.RoutingAnomaly,
            level: AetherThreatLevel.Medium));   // 0.80 → 0.70 (crosses 0.75)

        Assert.Single(consumer.Received);
        Assert.Equal(SecurityDirectiveKind.ElevateMonitoring,
            consumer.Received[0].Kind);
        await svc.StopAsync();
    }

    [Fact] public async Task SecurityEvent_CrossesAvoidNode_IssuesAvoidDirective()
    {
        var opts     = Make.Opts(elevate: 0.75, avoid: 0.50, quarantine: 0.25, initial: 0.60);
        var (svc, _, pub) = BuildSvc(opts);
        var consumer = new CapturingConsumer();
        pub.Subscribe(consumer);
        var tel = new ManualTelemetry();
        await svc.StartAsync(tel);

        // 0.60 − 0.30 (Intrusion/High = 0.15×2) = 0.30 → crosses 0.50
        tel.FireSecurity(Make.Sec("n",
            kind: AetherSecurityEventKind.IntrusionSignal,
            level: AetherThreatLevel.High));

        Assert.Single(consumer.Received);
        Assert.Equal(SecurityDirectiveKind.AvoidNode, consumer.Received[0].Kind);
        await svc.StopAsync();
    }

    [Fact] public async Task SecurityEvent_CrossesQuarantine_IssuesQuarantineDirective()
    {
        var opts     = Make.Opts(elevate: 0.75, avoid: 0.50, quarantine: 0.25, initial: 0.30);
        var (svc, _, pub) = BuildSvc(opts);
        var consumer = new CapturingConsumer();
        pub.Subscribe(consumer);
        var tel = new ManualTelemetry();
        await svc.StartAsync(tel);

        // 0.30 − 0.45 (Intrusion/Critical = 0.15×3) → 0.0, crosses 0.25
        tel.FireSecurity(Make.Sec("n",
            kind: AetherSecurityEventKind.IntrusionSignal,
            level: AetherThreatLevel.Critical));

        Assert.Single(consumer.Received);
        Assert.Equal(SecurityDirectiveKind.QuarantineNode,
            consumer.Received[0].Kind);
        await svc.StopAsync();
    }

    [Fact] public async Task SecurityEvent_AlreadyBelowThreshold_NoNewDirective()
    {
        // Start below quarantine threshold; no crossing can occur downward
        var opts     = Make.Opts(elevate: 0.75, avoid: 0.50, quarantine: 0.25, initial: 0.10);
        var (svc, _, pub) = BuildSvc(opts);
        var consumer = new CapturingConsumer();
        pub.Subscribe(consumer);
        var tel = new ManualTelemetry();
        await svc.StartAsync(tel);

        tel.FireSecurity(Make.Sec("n",
            kind: AetherSecurityEventKind.RoutingAnomaly,
            level: AetherThreatLevel.Low));   // −0.05, stays ≤ 0.25

        Assert.Empty(consumer.Received);
        await svc.StopAsync();
    }

    [Fact] public async Task SubscribeToDirectives_ReceivesDirectives()
    {
        var opts     = Make.Opts(initial: 0.80);
        var (svc, _, _) = BuildSvc(opts);
        var consumer = new CapturingConsumer();
        var tel      = new ManualTelemetry();
        await svc.StartAsync(tel);
        svc.SubscribeToDirectives(consumer);

        tel.FireSecurity(Make.Sec("n",
            kind: AetherSecurityEventKind.RoutingAnomaly,
            level: AetherThreatLevel.Medium));   // 0.80 → 0.70

        Assert.NotEmpty(consumer.Received);
        await svc.StopAsync();
    }

    [Fact] public async Task UnsubscribeHandle_StopsDelivery()
    {
        var opts     = Make.Opts(initial: 0.80);
        var (svc, _, _) = BuildSvc(opts);
        var consumer = new CapturingConsumer();
        var tel      = new ManualTelemetry();
        await svc.StartAsync(tel);
        var handle = svc.SubscribeToDirectives(consumer);
        handle.Dispose();

        tel.FireSecurity(Make.Sec("n",
            kind: AetherSecurityEventKind.RoutingAnomaly,
            level: AetherThreatLevel.Medium));

        Assert.Empty(consumer.Received);
        await svc.StopAsync();
    }

    [Fact] public async Task GetPostureAsync_NoNodes_ReturnsThreatNone()
    {
        var (svc, _, _) = BuildSvc();
        await svc.StartAsync(new ManualTelemetry());
        var posture = await svc.GetPostureAsync();
        Assert.Equal(AetherThreatLevel.None, posture.OverallThreatLevel);
        await svc.StopAsync();
    }

    [Fact] public async Task GetPostureAsync_QuarantinedNode_CountedCorrectly()
    {
        var opts     = Make.Opts(quarantine: 0.25, initial: 0.10);   // below quarantine
        var (svc, reg, _) = BuildSvc(opts);
        reg.ApplyDegradation(Make.Sec("q-node"), 0.0);               // create entry at 0.10
        await svc.StartAsync(new ManualTelemetry());

        var posture = await svc.GetPostureAsync();
        Assert.Equal(1, posture.QuarantinedNodeCount);
        await svc.StopAsync();
    }

    [Fact] public async Task GetPostureAsync_IsActive_FalseAfterStop()
    {
        var (svc, _, _) = BuildSvc();
        await svc.StartAsync(new ManualTelemetry());
        await svc.StopAsync();
        var posture = await svc.GetPostureAsync();
        Assert.False(posture.IsActive);
    }
}

// ─── AetherIntelligenceService tests ─────────────────────────────────────────

public sealed class AetherIntelligenceServiceTests
{
    private static AetherIntelligenceService BuildSvc(
        NodeTrustRegistry reg, SecurityOptions? opts = null) =>
        new(reg, opts ?? Make.Opts());

    [Fact] public async Task NetworkHealth_NoNodes_ReturnsOnePointZero()
    {
        var reg = Make.Registry();
        var svc = BuildSvc(reg);
        var report = await svc.GetNetworkHealthAsync();
        Assert.Equal(1.0, report.OverallScore, precision: 5);
        Assert.True(report.IsValid);
    }

    [Fact] public async Task NetworkHealth_AllTrusted_HighScore()
    {
        var reg = Make.Registry();
        reg.GetOrCreate("a");   // both at 1.0
        reg.GetOrCreate("b");
        var svc    = BuildSvc(reg);
        var report = await svc.GetNetworkHealthAsync();
        Assert.True(report.OverallScore > 0.9);
        Assert.Equal(2, report.TrustedNodeCount);
    }

    [Fact] public async Task NetworkHealth_DegradedNodes_LowerScore()
    {
        var reg = Make.Registry();
        reg.ApplyDegradation(Make.Sec("n1"), 0.60);   // 0.40
        reg.ApplyDegradation(Make.Sec("n2"), 0.60);   // 0.40
        var svc    = BuildSvc(reg);
        var report = await svc.GetNetworkHealthAsync();
        Assert.True(report.OverallScore < 0.5);
    }

    [Fact] public async Task AssessThreat_FullTrust_NoneLevel()
    {
        var reg = Make.Registry();
        var svc = BuildSvc(reg);
        var assessment = await svc.AssessThreatAsync("new-node");
        Assert.Equal(AetherThreatLevel.None, assessment.Level);
        Assert.Equal(0.0, assessment.ThreatConfidence, precision: 5);
    }

    [Fact] public async Task AssessThreat_ScoreAt0_40_IsHighLevel()
    {
        var reg = Make.Registry();
        reg.ApplyDegradation(Make.Sec("n"), 0.60);    // score → 0.40
        var svc = BuildSvc(reg);
        var assessment = await svc.AssessThreatAsync("n");
        Assert.Equal(AetherThreatLevel.High, assessment.Level);
        Assert.True(assessment.IsValid);
    }

    [Fact] public async Task AssessThreat_ScoreAtZero_CriticalLevel()
    {
        var reg = Make.Registry();
        reg.ApplyDegradation(Make.Sec("n"), 1.0);    // clamp to 0
        var svc = BuildSvc(reg);
        var assessment = await svc.AssessThreatAsync("n");
        Assert.Equal(AetherThreatLevel.Critical, assessment.Level);
    }

    [Fact] public async Task AssessThreat_UnknownNode_ZeroConfidence()
    {
        var reg = Make.Registry();
        var svc = BuildSvc(reg);
        var assessment = await svc.AssessThreatAsync("ghost");
        Assert.Equal(0.0, assessment.ThreatConfidence, precision: 5);
    }

    [Fact] public async Task AssessThreat_WithIndicators_ConfidenceBoostAboveDeficit()
    {
        var reg = Make.Registry(Make.Opts(initial: 0.95));
        // inject 3 auth attempts to trigger repeated-auth-attempts indicator
        for (var i = 0; i < 3; i++)
            reg.ApplyDegradation(
                Make.Sec("n",
                    kind: AetherSecurityEventKind.NodeAuthAttempt,
                    level: AetherThreatLevel.Low,
                    at: DateTimeOffset.UtcNow.AddSeconds(-i)),
                0.01);

        var svc        = BuildSvc(reg);
        var assessment = await svc.AssessThreatAsync("n");
        Assert.True(assessment.Indicators.Contains("repeated-auth-attempts"));
    }

    [Fact] public async Task RoutingAdvice_TrustedDest_DirectPath()
    {
        var reg    = Make.Registry();
        var svc    = BuildSvc(reg);
        var advice = await svc.GetRoutingAdviceAsync("dest-node");
        Assert.Contains("dest-node", advice.RecommendedPath);
        Assert.Equal("dest-node", advice.DestinationNodeId);
    }

    [Fact] public async Task RoutingAdvice_DegradedDest_EmptyRecommendedPath()
    {
        var reg = Make.Registry(Make.Opts(avoid: 0.50));
        reg.ApplyDegradation(Make.Sec("bad-node"), 0.60);   // score 0.40 ≤ 0.50
        var svc    = BuildSvc(reg);
        var advice = await svc.GetRoutingAdviceAsync("bad-node");
        Assert.Empty(advice.RecommendedPath);
    }

    [Fact] public async Task RoutingAdvice_AvoidNodesList_IncludesDegradedNodes()
    {
        var reg = Make.Registry(Make.Opts(avoid: 0.50));
        reg.ApplyDegradation(Make.Sec("bad"), 0.60);   // 0.40 → in avoid list
        var svc    = BuildSvc(reg);
        var advice = await svc.GetRoutingAdviceAsync("dest");
        Assert.Contains("bad", advice.AvoidNodes);
    }

    [Fact] public async Task StreamTrustScores_YieldsUpdatesOnDegradation()
    {
        var reg = Make.Registry();
        var svc = BuildSvc(reg);

        // Write to the channel before iterating — first read returns immediately
        reg.ApplyDegradation(Make.Sec("stream-node"), 0.10);

        TrustScoreUpdate? update = null;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await foreach (var u in svc.StreamTrustScoresAsync(cts.Token))
        {
            update = u;
            break;   // take only the first item
        }

        Assert.NotNull(update);
        Assert.Equal("stream-node", update!.NodeId);
    }
}
