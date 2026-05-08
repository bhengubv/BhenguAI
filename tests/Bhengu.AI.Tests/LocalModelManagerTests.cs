using System;
using System.IO;
using System.Threading.Tasks;
using Bhengu.AI.Core;
using Xunit;

namespace Bhengu.AI.Tests;

public sealed class LocalModelManagerTests : IDisposable
{
    // Use a real temp directory so the constructor's Directory.Exists check passes.
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // -----------------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------------

    [Fact]
    public void Constructor_IModelDownloader_NullDownloader_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new LocalModelManager((IModelDownloader)null!, _tempDir));
    }

    [Fact]
    public void Constructor_IModelDownloader_ValidArgs_CreatesDirectory()
    {
        var dl = new FakeModelDownloader();
        var dir = Path.Combine(_tempDir, "sub1");

        Assert.False(Directory.Exists(dir));
        using var mgr = new LocalModelManager(dl, dir);
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void Constructor_Uri_NullUri_CreatesManager()
    {
        // null URI means no downloader — manager is constructed but can't download.
        var dir = Path.Combine(_tempDir, "sub2");
        using var mgr = new LocalModelManager((Uri?)null, dir);
        Assert.NotNull(mgr);
    }

    [Fact]
    public void Constructor_Uri_NonNullUri_CreatesDownloader()
    {
        var uri = new Uri("https://huggingface.co/");
        var dir = Path.Combine(_tempDir, "sub3");
        using var mgr = new LocalModelManager(uri, dir);
        Assert.NotNull(mgr);
    }

    // -----------------------------------------------------------------------
    // GetModelPathAsync — dispose guard
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetModelPathAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var dl = new FakeModelDownloader();
        var mgr = new LocalModelManager(dl, _tempDir);
        mgr.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            mgr.GetModelPathAsync("any-model"));
    }

    // -----------------------------------------------------------------------
    // GetModelPathAsync — no downloader + missing model
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetModelPathAsync_NoDownloaderAndMissingModel_ThrowsInvalidOperation()
    {
        // null URI means no downloader configured
        var dir = Path.Combine(_tempDir, "sub4");
        using var mgr = new LocalModelManager((Uri?)null, dir);

        // Model directory doesn't exist and there's no downloader.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mgr.GetModelPathAsync("missing-model"));
    }

    // -----------------------------------------------------------------------
    // GetModelPathAsync — downloader invoked when model absent
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetModelPathAsync_ModelAbsent_DelegatesToDownloader()
    {
        var dl = new FakeModelDownloader();
        var dir = Path.Combine(_tempDir, "sub5");
        using var mgr = new LocalModelManager(dl, dir);

        // The fake downloader won't actually create pytorch_model.bin,
        // so GetModelPathAsync will try to download but succeed from the
        // downloader's perspective. The call completes without throwing.
        // (The post-download checksum check requires the file to exist;
        //  we don't pass a checksum so it's skipped.)
        var path = await mgr.GetModelPathAsync("test-model");
        Assert.Equal(1, dl.DownloadCallCount);
        Assert.NotEmpty(path);
    }

    // -----------------------------------------------------------------------
    // Dispose idempotency
    // -----------------------------------------------------------------------

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var dl = new FakeModelDownloader();
        var mgr = new LocalModelManager(dl, _tempDir);
        mgr.Dispose();
        var ex = Record.Exception(() => mgr.Dispose());
        Assert.Null(ex);
    }

    // -----------------------------------------------------------------------
    // Helper: fake IModelDownloader
    // -----------------------------------------------------------------------

    private sealed class FakeModelDownloader : IModelDownloader
    {
        public int DownloadCallCount { get; private set; }

        public Task DownloadModelAsync(string modelId, string localPath,
            System.Threading.CancellationToken ct = default)
        {
            DownloadCallCount++;
            return Task.CompletedTask;
        }

        public Task<string> DownloadFromCandidatesAsync(
            System.Collections.Generic.IReadOnlyList<string> candidateUrls,
            string localFilePath,
            IProgress<DownloadProgress>? progress = null,
            System.Threading.CancellationToken ct = default)
            => Task.FromResult("fake");
    }
}
