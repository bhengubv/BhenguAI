using Circle.AI.Companion;
using System.Runtime.CompilerServices;
using System.Text;

namespace Circle.AI.Wearable;

/// <summary>
/// Wraps <see cref="ICompanionSession"/> with wearable-specific biometric context.
/// Injects heart rate, step count, and workout state into each message so the
/// Companion can respond with health-aware, appropriately concise replies.
/// </summary>
public sealed class WearableCompanionAdapter : ICompanionSession
{
    private readonly ICompanionSession _inner;

    public WearableContext? CurrentContext { get; set; }

    public WearableCompanionAdapter(ICompanionSession inner)
        => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public string SessionId => _inner.SessionId;
    public string IdentityId => _inner.IdentityId;
    public InterfaceKind Interface => InterfaceKind.Wearable;
    public IReadOnlyList<CompanionTurn> History => _inner.History;
    public event EventHandler<CompanionProactiveEvent>? ProactiveMessageReady
    {
        add    => _inner.ProactiveMessageReady += value;
        remove => _inner.ProactiveMessageReady -= value;
    }

    public CompanionContext GetContext() => _inner.GetContext();
    public Task RefreshContextAsync(CancellationToken ct = default) => _inner.RefreshContextAsync(ct);
    public Task SignalFeedbackAsync(bool positive, string? note = null, CancellationToken ct = default)
        => _inner.SignalFeedbackAsync(positive, note, ct);

    public Task<string> SendAsync(string message, CancellationToken ct = default)
        => _inner.SendAsync(EnrichMessage(message), ct);

    public IAsyncEnumerable<string> StreamAsync(
        string message, [EnumeratorCancellation] CancellationToken ct = default)
        => _inner.StreamAsync(EnrichMessage(message), ct);

    public Task<string> AgentAsync(string instruction, CancellationToken ct = default)
        => _inner.AgentAsync(EnrichMessage(instruction), ct);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    private string EnrichMessage(string message)
    {
        var ctx = CurrentContext;
        if (ctx is null) return message;

        var sb = new StringBuilder(message);
        sb.AppendLine();
        sb.Append("[Biometrics] ");
        if (ctx.HeartRateBpm.HasValue)    sb.Append($"HR:{ctx.HeartRateBpm:F0}bpm ");
        if (ctx.StepCountToday.HasValue)  sb.Append($"Steps:{ctx.StepCountToday} ");
        if (ctx.SpO2Percent.HasValue)     sb.Append($"SpO₂:{ctx.SpO2Percent:F0}% ");
        if (ctx.IsWorkoutActive)          sb.Append("Workout:active ");
        return sb.ToString().TrimEnd();
    }
}
