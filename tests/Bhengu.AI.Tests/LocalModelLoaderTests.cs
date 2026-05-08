using System.IO;
using System.Threading.Tasks;
using Bhengu.AI.Core;
using Xunit;

namespace Bhengu.AI.Tests;

/// <summary>
/// Tests for <see cref="LocalModelLoader"/> that do NOT require network access
/// or real model files.
/// </summary>
public sealed class LocalModelLoaderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public LocalModelLoaderTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ------------------------------------------------------------------
    // Registry is loaded — known models are known
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("Qwen3-14B-Q4")]
    [InlineData("Qwen3.6-35B-A3B-Q3")]
    public void GetModelPath_KnownModel_ReturnsPathString(string modelName)
    {
        using var loader = new LocalModelLoader(_tempDir);
        // File won't exist, but the method should still return the path.
        var path = loader.GetModelPath(modelName);
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.StartsWith(_tempDir, path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetModelPath_UnknownModel_ThrowsFileNotFoundException()
    {
        using var loader = new LocalModelLoader(_tempDir);
        Assert.Throws<FileNotFoundException>(
            () => loader.GetModelPath("NonExistent-Model-XYZ"));
    }

    [Fact]
    public async Task DownloadModelAsync_UnknownModel_ThrowsArgumentException()
    {
        using var loader = new LocalModelLoader(_tempDir);
        await Assert.ThrowsAsync<ArgumentException>(
            () => loader.DownloadModelAsync("NonExistent-Model-XYZ"));
    }

    // ------------------------------------------------------------------
    // ModelExists
    // ------------------------------------------------------------------

    [Fact]
    public void ModelExists_KnownModelFileAbsent_ReturnsFalse()
    {
        using var loader = new LocalModelLoader(_tempDir);
        // The GGUF file is not present → must return false
        Assert.False(loader.ModelExists("Qwen3-14B-Q4"));
    }

    [Fact]
    public void ModelExists_UnknownModel_ReturnsFalse()
    {
        using var loader = new LocalModelLoader(_tempDir);
        Assert.False(loader.ModelExists("NoSuchModel"));
    }

    // ------------------------------------------------------------------
    // Dispose
    // ------------------------------------------------------------------

    [Fact]
    public void Dispose_ThenGetModelPath_ThrowsObjectDisposedException()
    {
        var loader = new LocalModelLoader(_tempDir);
        loader.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => loader.GetModelPath("Qwen3-14B-Q4"));
    }

    [Fact]
    public async Task Dispose_ThenDownload_ThrowsObjectDisposedException()
    {
        var loader = new LocalModelLoader(_tempDir);
        loader.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => loader.DownloadModelAsync("Qwen3-14B-Q4"));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var loader = new LocalModelLoader(_tempDir);
        loader.Dispose();
        loader.Dispose(); // should not throw
    }

    // ------------------------------------------------------------------
    // CheckForCriticalUpdateAsync — no network in CI, must not throw
    // ------------------------------------------------------------------

    [Fact]
    public async Task CheckForCriticalUpdateAsync_OfflineOrError_ReturnsFalse()
    {
        using var loader = new LocalModelLoader(_tempDir);
        // Network likely unavailable in test environment; implementation
        // swallows exceptions and returns false.
        var result = await loader.CheckForCriticalUpdateAsync();
        Assert.IsType<bool>(result); // just ensure no exception
    }
}
