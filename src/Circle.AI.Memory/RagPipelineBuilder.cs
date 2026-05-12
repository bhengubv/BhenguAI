// RagPipelineBuilder.cs
//
// Fluent factory for constructing a RagContextBuilder with sensible defaults.
// Provides convenience methods for common store types (in-memory, SQLite)
// so callers don't need to manually construct store instances.

using System;
using Circle.AI.Embeddings;

namespace Circle.AI.Memory;

/// <summary>
/// Fluent builder for constructing a <see cref="RagContextBuilder"/> with
/// an episodic store, optional embedder, and tuning parameters.
/// </summary>
/// <example>
/// <code>
/// var rag = RagPipelineBuilder.Create()
///     .WithSqliteStore("Data Source=episodes.db")
///     .WithTopK(10)
///     .WithMaxCharsPerEntry(500)
///     .Build();
///
/// var context = await rag.BuildContextAsync("user query");
/// </code>
/// </example>
public sealed class RagPipelineBuilder
{
    private IEpisodicMemoryStore? _store;
    private ITextEmbedder? _embedder;
    private int _topK = 5;
    private int _maxCharsPerEntry = 300;

    private RagPipelineBuilder() { }

    /// <summary>
    /// Creates a new <see cref="RagPipelineBuilder"/> instance.
    /// </summary>
    /// <returns>A new builder ready for configuration.</returns>
    public static RagPipelineBuilder Create() => new();

    /// <summary>
    /// Sets the episodic memory store to retrieve past exchanges from.
    /// </summary>
    /// <param name="store">The episodic memory store. Must not be <c>null</c>.</param>
    /// <returns>This builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="store"/> is <c>null</c>.
    /// </exception>
    public RagPipelineBuilder WithStore(IEpisodicMemoryStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        return this;
    }

    /// <summary>
    /// Convenience method that creates a <see cref="SqliteEpisodicStore"/>
    /// from the given connection string and uses it as the episodic store.
    /// </summary>
    /// <param name="connectionString">
    /// SQLite connection string, e.g. <c>"Data Source=episodes.db"</c> or
    /// <c>"Data Source=:memory:"</c> for tests.
    /// </param>
    /// <returns>This builder for chaining.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="connectionString"/> is <c>null</c> or empty.
    /// </exception>
    public RagPipelineBuilder WithSqliteStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        _store = new SqliteEpisodicStore(connectionString);
        return this;
    }

    /// <summary>
    /// Convenience method that creates an <see cref="InMemoryEpisodicStore"/>
    /// and uses it as the episodic store. Suitable for tests and short-lived
    /// processes where persistence is not needed.
    /// </summary>
    /// <returns>This builder for chaining.</returns>
    public RagPipelineBuilder WithInMemoryStore()
    {
        _store = new InMemoryEpisodicStore();
        return this;
    }

    /// <summary>
    /// Sets the text embedder for semantic similarity search. When not set,
    /// the <see cref="RagContextBuilder"/> falls back to recency-based retrieval.
    /// </summary>
    /// <param name="embedder">The text embedder to use. Must not be <c>null</c>.</param>
    /// <returns>This builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="embedder"/> is <c>null</c>.
    /// </exception>
    public RagPipelineBuilder WithEmbedder(ITextEmbedder embedder)
    {
        ArgumentNullException.ThrowIfNull(embedder);
        _embedder = embedder;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of relevant past episodes to include in the
    /// context block. Default 5.
    /// </summary>
    /// <param name="topK">Must be at least 1.</param>
    /// <returns>This builder for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="topK"/> is less than 1.
    /// </exception>
    public RagPipelineBuilder WithTopK(int topK)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(topK, 1);
        _topK = topK;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of characters taken from each episode's texts.
    /// Prevents any single episode from dominating the context window.
    /// Default 300.
    /// </summary>
    /// <param name="maxChars">Must be at least 50.</param>
    /// <returns>This builder for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxChars"/> is less than 50.
    /// </exception>
    public RagPipelineBuilder WithMaxCharsPerEntry(int maxChars)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChars, 50);
        _maxCharsPerEntry = maxChars;
        return this;
    }

    /// <summary>
    /// Builds the <see cref="RagContextBuilder"/> from the accumulated configuration.
    /// </summary>
    /// <returns>A configured <see cref="RagContextBuilder"/> ready for use.</returns>
    /// <exception cref="InvalidOperationException">
    /// No episodic store was configured. Call <see cref="WithStore"/>,
    /// <see cref="WithSqliteStore"/>, or <see cref="WithInMemoryStore"/> first.
    /// </exception>
    public RagContextBuilder Build()
    {
        if (_store is null)
            throw new InvalidOperationException(
                "An episodic memory store is required. Call WithStore(), " +
                "WithSqliteStore(), or WithInMemoryStore() before Build().");

        return new RagContextBuilder(_store, _embedder, _topK, _maxCharsPerEntry);
    }
}
