namespace Bhengu.AI.Security;

// ─────────────────────────────────────────────────────────────────────────────
// Fan-out publisher for PeerDirectives.
//
// Keeps a list of IPeerDirectiveConsumer subscriptions and fans every
// published directive out to all current subscribers. Concurrent subscribe,
// unsubscribe, and publish operations are all thread-safe.
//
// Transport-agnostic: no dependency on any transport package.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Manages <see cref="IPeerDirectiveConsumer"/> subscriptions and fans
/// published <see cref="PeerDirective"/> instances out to all subscribers.
/// </summary>
public sealed class DirectivePublisher
{
    private readonly object _lock = new();
    private readonly List<IPeerDirectiveConsumer> _consumers = new();

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Subscribes <paramref name="consumer"/> to receive directives.
    /// Dispose the returned handle to unsubscribe. Idempotent disposal.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="consumer"/> is null.
    /// </exception>
    public IDisposable Subscribe(IPeerDirectiveConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        lock (_lock) _consumers.Add(consumer);
        return new SubscriptionHandle(this, consumer);
    }

    /// <summary>
    /// Publishes <paramref name="directive"/> to all current subscribers.
    /// A snapshot is taken under the lock; callbacks fire outside it.
    /// </summary>
    public void Publish(PeerDirective directive)
    {
        IPeerDirectiveConsumer[] snapshot;
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

    private void Unsubscribe(IPeerDirectiveConsumer consumer)
    {
        lock (_lock) _consumers.Remove(consumer);
    }

    private sealed class SubscriptionHandle : IDisposable
    {
        private readonly DirectivePublisher _publisher;
        private readonly IPeerDirectiveConsumer _consumer;
        private int _disposed;

        internal SubscriptionHandle(
            DirectivePublisher publisher, IPeerDirectiveConsumer consumer)
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
