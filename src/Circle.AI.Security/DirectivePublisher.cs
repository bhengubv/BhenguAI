namespace Circle.AI.Security;

using Circle.AI.Aether;

// ─────────────────────────────────────────────────────────────────────────────
// Fan-out publisher for SecurityDirectives.
//
// Keeps a list of ISecurityDirectiveConsumer subscriptions and fans every
// published directive out to all current subscribers. Concurrent subscribe,
// unsubscribe, and publish operations are all safe.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Manages <see cref="ISecurityDirectiveConsumer"/> subscriptions and fans
/// published <see cref="SecurityDirective"/> instances out to all subscribers.
/// </summary>
public sealed class DirectivePublisher
{
    private readonly object _lock = new();
    private readonly List<ISecurityDirectiveConsumer> _consumers = new();

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Subscribes <paramref name="consumer"/> to receive directives.
    /// Dispose the returned handle to unsubscribe. Idempotent disposal.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="consumer"/> is null.
    /// </exception>
    public IDisposable Subscribe(ISecurityDirectiveConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        lock (_lock) _consumers.Add(consumer);
        return new SubscriptionHandle(this, consumer);
    }

    /// <summary>
    /// Publishes <paramref name="directive"/> to all current subscribers.
    /// Snapshot is taken under the lock; callbacks fire outside it.
    /// </summary>
    public void Publish(SecurityDirective directive)
    {
        ISecurityDirectiveConsumer[] snapshot;
        lock (_lock) snapshot = [.. _consumers];

        foreach (var c in snapshot)
            c.OnDirective(directive);
    }

    /// <summary>Number of currently active subscribers. Useful in tests.</summary>
    public int SubscriberCount
    {
        get { lock (_lock) return _consumers.Count; }
    }

    // ─── Private ─────────────────────────────────────────────────────────────

    private void Unsubscribe(ISecurityDirectiveConsumer consumer)
    {
        lock (_lock) _consumers.Remove(consumer);
    }

    private sealed class SubscriptionHandle : IDisposable
    {
        private readonly DirectivePublisher _publisher;
        private readonly ISecurityDirectiveConsumer _consumer;
        private int _disposed;

        internal SubscriptionHandle(
            DirectivePublisher publisher, ISecurityDirectiveConsumer consumer)
        {
            _publisher = publisher;
            _consumer  = consumer;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _publisher.Unsubscribe(_consumer);
        }
    }
}
