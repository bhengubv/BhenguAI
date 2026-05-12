using System;
using System.Threading.Tasks;
using Bhengu.AI.Embeddings;
using Bhengu.AI.Core;
using Xunit;

namespace Bhengu.AI.Tests;

public sealed class TextEmbedderTests
{
    // -----------------------------------------------------------------------
    // Constructor guards
    // -----------------------------------------------------------------------

    [Fact]
    public void Constructor_NullModelManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TextEmbedder(null!, new byte[] { 0x01 }));
    }

    [Fact]
    public void Constructor_NullChecksum_Throws()
    {
        var mgr = new FakeModelManager();
        Assert.Throws<ArgumentNullException>(() =>
            new TextEmbedder(mgr, null!));
    }

    [Fact]
    public void Constructor_ValidArgs_Succeeds()
    {
        using var embedder = new TextEmbedder(new FakeModelManager(), new byte[] { 0xAB });
        Assert.NotNull(embedder);
    }

    // -----------------------------------------------------------------------
    // GenerateAsync — argument guards
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_NullText_Throws()
    {
        using var embedder = new TextEmbedder(new FakeModelManager(), new byte[] { 0x01 });
        // Null is coerced to whitespace check; ArgumentException or ArgumentNullException accepted.
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            embedder.GenerateAsync(null!));
    }

    [Fact]
    public async Task GenerateAsync_EmptyText_ThrowsArgumentException()
    {
        using var embedder = new TextEmbedder(new FakeModelManager(), new byte[] { 0x01 });
        await Assert.ThrowsAsync<ArgumentException>(() =>
            embedder.GenerateAsync(""));
    }

    [Fact]
    public async Task GenerateAsync_WhitespaceText_ThrowsArgumentException()
    {
        using var embedder = new TextEmbedder(new FakeModelManager(), new byte[] { 0x01 });
        await Assert.ThrowsAsync<ArgumentException>(() =>
            embedder.GenerateAsync("   "));
    }

    // -----------------------------------------------------------------------
    // GenerateAsync — factory-injection path (no native library required)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_WithFakeBackend_ReturnsFloatArray()
    {
        // The internal constructor accepts a factory so tests bypass the
        // native llama.cpp backend.
        var mgr = new FakeModelManager();
        using var embedder = new TextEmbedder(
            mgr,
            new byte[] { 0x01 },
            _ => new FakeEmbeddingBackend(dimension: 4));

        var result = await embedder.GenerateAsync("hello world");

        Assert.NotNull(result);
        Assert.Equal(4, result.Length);
        // The FakeEmbeddingBackend returns L2-normalised vectors so the
        // magnitude must be approximately 1.
        double norm = 0;
        foreach (var x in result) norm += (double)x * x;
        Assert.InRange(Math.Sqrt(norm), 0.99, 1.01);
    }

    [Fact]
    public async Task GenerateAsync_WithFakeBackend_ConcurrentCalls_UseSameBackend()
    {
        // Verify the semaphore + lazy-init path handles concurrent callers.
        var mgr = new FakeModelManager();
        int factoryCallCount = 0;

        using var embedder = new TextEmbedder(
            mgr,
            new byte[] { 0x01 },
            _ =>
            {
                System.Threading.Interlocked.Increment(ref factoryCallCount);
                return new FakeEmbeddingBackend(4);
            });

        // Fire 5 concurrent calls.
        var tasks = new Task<float[]>[5];
        for (int i = 0; i < tasks.Length; i++)
            tasks[i] = embedder.GenerateAsync("ping");

        await Task.WhenAll(tasks);

        // Factory must have been called exactly once regardless of concurrency.
        Assert.Equal(1, factoryCallCount);
    }

    [Fact]
    public async Task GenerateAsync_ProductionPath_ThrowsWhenModelFileMissing()
    {
        // The public (production) constructor uses LlamaEmbeddingBackend.
        // With FakeModelManager returning a non-existent path, the backend
        // constructor throws FileNotFoundException.
        using var embedder = new TextEmbedder(
            new FakeModelManager("nonexistent/model.gguf"),
            new byte[] { 0x01 });

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            embedder.GenerateAsync("hello"));
    }

    // -----------------------------------------------------------------------
    // Dispose — guard and idempotency
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var embedder = new TextEmbedder(new FakeModelManager(), new byte[] { 0x01 });
        embedder.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            embedder.GenerateAsync("hello"));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var embedder = new TextEmbedder(new FakeModelManager(), new byte[] { 0x01 });
        embedder.Dispose();
        var ex = Record.Exception(() => embedder.Dispose());
        Assert.Null(ex);
    }
}
