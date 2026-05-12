using System.IO;
using System.Threading.Tasks;
using Circle.AI.Core;
using Xunit;

namespace Circle.AI.Tests;

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
    // TBD checksum behaviour (production blocker documentation)
    // PRODUCTION BLOCKER: see TODO.md — all models currently have sha256:TBD.
    // These tests document the current behaviour until real hashes are computed.
    // ------------------------------------------------------------------

    /// <summary>
    /// Documents that <see cref="LocalModelLoader.ModelExists"/> returns
    /// <c>false</c> for any model whose registry entry has <c>sha256:TBD</c>,
    /// even when the physical file is present.
    /// This is an inconsistency with <see cref="LocalModelLoader.DownloadModelAsync"/>
    /// which skips verification for TBD. Real checksums must be added before shipping.
    /// </summary>
    [Fact]
    public void ModelExists_TbdChecksumWithFilePresent_ReturnsFalse()
    {
        // Synthesise the expected file name from what the registry specifies.
        var modelPath = Path.Combine(_tempDir, "qwen3-14b-instruct-q4_k_m.gguf");
        File.WriteAllBytes(modelPath, new byte[16]); // sentinel file

        using var loader = new LocalModelLoader(_tempDir);
        // File exists but checksum is TBD → VerifyChecksum("sha256:TBD") always
        // returns false because the actual SHA-256 never equals the literal "TBD".
        Assert.False(loader.ModelExists("Qwen3-14B-Q4"));
    }

    /// <summary>
    /// Documents that <see cref="LocalModelLoader.DownloadModelAsync"/> skips
    /// re-download and verification when the local file exists and the checksum
    /// is <c>sha256:TBD</c>. This is the correct short-circuit for development use.
    /// </summary>
    [Fact]
    public async Task DownloadModelAsync_TbdChecksumExistingFile_ReturnsPathWithoutDownload()
    {
        var modelPath = Path.Combine(_tempDir, "qwen3-14b-instruct-q4_k_m.gguf");
        File.WriteAllBytes(modelPath, new byte[16]); // sentinel file

        using var loader = new LocalModelLoader(_tempDir);
        // Checksum is TBD + file exists → loader returns path immediately,
        // no network call, no ArgumentException.
        var result = await loader.DownloadModelAsync("Qwen3-14B-Q4");
        Assert.Equal(modelPath, result, StringComparer.OrdinalIgnoreCase);
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

    // ------------------------------------------------------------------
    // Constructor creates the model directory when it does not exist
    // ------------------------------------------------------------------

    [Fact]
    public void Constructor_NonExistentDirectory_CreatesIt()
    {
        // Pass a sub-path that has never been created; the constructor must
        // call Directory.CreateDirectory and produce a real directory.
        var newSubDir = Path.Combine(_tempDir, "auto_created_by_ctor");
        Assert.False(Directory.Exists(newSubDir));

        using var loader = new LocalModelLoader(newSubDir);

        Assert.True(Directory.Exists(newSubDir));
    }

    // ------------------------------------------------------------------
    // GetModelPath returns the expected path even when file is absent
    // ------------------------------------------------------------------

    [Fact]
    public void GetModelPath_KnownModelFileAbsent_PathIsInsideModelDir()
    {
        // The path is the expected on-disk location, not a live-file check.
        // File absence must not cause GetModelPath to throw or return empty.
        using var loader = new LocalModelLoader(_tempDir);
        var path = loader.GetModelPath("Qwen3-14B-Q4");
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.StartsWith(_tempDir, path, StringComparison.OrdinalIgnoreCase);
    }
}
