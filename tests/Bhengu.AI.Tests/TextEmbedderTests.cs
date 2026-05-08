using System;
using System.Threading.Tasks;
using Bhengu.AI.Embeddings;
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
    // GenerateAsync — pending backend stub
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_ValidText_ThrowsNotSupportedException()
    {
        using var embedder = new TextEmbedder(new FakeModelManager(), new byte[] { 0x01 });
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            embedder.GenerateAsync("hello world"));
        Assert.Contains("sovereign", ex.Message, StringComparison.OrdinalIgnoreCase);
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
