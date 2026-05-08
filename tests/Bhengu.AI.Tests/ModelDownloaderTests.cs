using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bhengu.AI.Core;
using Xunit;

namespace Bhengu.AI.Tests;

public sealed class ModelDownloaderTests
{
    // -----------------------------------------------------------------------
    // Constructor guards
    // -----------------------------------------------------------------------

    [Fact]
    public void Constructor_NullSources_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ModelDownloader(null!));
    }

    [Fact]
    public void Constructor_EmptySources_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new ModelDownloader(Array.Empty<IModelSource>()));
    }

    [Fact]
    public void Constructor_ValidSources_Succeeds()
    {
        var src = new FakeModelSource();
        using var dl = new ModelDownloader(new[] { src });
        Assert.NotNull(dl);
    }

    // -----------------------------------------------------------------------
    // DownloadModelAsync — argument / dispose guards
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DownloadModelAsync_NullModelId_Throws()
    {
        var src = new FakeModelSource();
        using var dl = new ModelDownloader(new[] { src });
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            dl.DownloadModelAsync(null!, Path.GetTempPath()));
    }

    [Fact]
    public async Task DownloadModelAsync_WhitespaceModelId_Throws()
    {
        var src = new FakeModelSource();
        using var dl = new ModelDownloader(new[] { src });
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            dl.DownloadModelAsync("   ", Path.GetTempPath()));
    }

    [Fact]
    public async Task DownloadModelAsync_NullLocalPath_Throws()
    {
        var src = new FakeModelSource();
        using var dl = new ModelDownloader(new[] { src });
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            dl.DownloadModelAsync("some-model", null!));
    }

    [Fact]
    public async Task DownloadModelAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var src = new FakeModelSource();
        var dl = new ModelDownloader(new[] { src });
        dl.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            dl.DownloadModelAsync("any", Path.GetTempPath()));
    }

    /// <summary>
    /// The embedded registry in the test binary will not contain "no-such-model".
    /// Expect <see cref="KeyNotFoundException"/>.
    /// </summary>
    [Fact]
    public async Task DownloadModelAsync_UnknownModel_ThrowsKeyNotFoundException()
    {
        var src = new FakeModelSource();
        using var dl = new ModelDownloader(new[] { src });
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            dl.DownloadModelAsync("no-such-model-xyz", Path.GetTempPath()));
    }

    // -----------------------------------------------------------------------
    // DownloadFromCandidatesAsync — argument / dispose guards
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DownloadFromCandidatesAsync_NullUrls_Throws()
    {
        var src = new FakeModelSource();
        using var dl = new ModelDownloader(new[] { src });
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            dl.DownloadFromCandidatesAsync(null!, Path.GetTempFileName()));
    }

    [Fact]
    public async Task DownloadFromCandidatesAsync_EmptyUrlList_Throws()
    {
        var src = new FakeModelSource();
        using var dl = new ModelDownloader(new[] { src });
        await Assert.ThrowsAsync<ArgumentException>(() =>
            dl.DownloadFromCandidatesAsync(Array.Empty<string>(), Path.GetTempFileName()));
    }

    [Fact]
    public async Task DownloadFromCandidatesAsync_NullLocalPath_Throws()
    {
        var src = new FakeModelSource();
        using var dl = new ModelDownloader(new[] { src });
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            dl.DownloadFromCandidatesAsync(new[] { "https://fakehost/file.bin" }, null!));
    }

    [Fact]
    public async Task DownloadFromCandidatesAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var src = new FakeModelSource();
        var dl = new ModelDownloader(new[] { src });
        dl.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            dl.DownloadFromCandidatesAsync(new[] { "https://fakehost/f.bin" }, "/tmp/x"));
    }

    // -----------------------------------------------------------------------
    // DownloadFromCandidatesAsync — routing behaviour
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DownloadFromCandidatesAsync_NoMatchingSource_ThrowsInvalidOperation()
    {
        // Source name "fakehost" won't match the URL "unmatched-cdn.io"
        var src = new FakeModelSource("fakehost");
        using var dl = new ModelDownloader(new[] { src });
        var tmp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dl.DownloadFromCandidatesAsync(
                new[] { "https://unmatched-cdn.io/model.bin" }, tmp));
    }

    [Fact]
    public async Task DownloadFromCandidatesAsync_MatchingSource_DelegatesToSource()
    {
        // Source name contains "fakehost" which will be a substring of the URL host.
        var src = new FakeModelSource("fakehost");
        using var dl = new ModelDownloader(new[] { src });
        var tmp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            var winner = await dl.DownloadFromCandidatesAsync(
                new[] { "https://fakehost.example/model.bin" }, tmp);

            Assert.Equal("fakehost", winner);
            Assert.Equal(1, src.DownloadCallCount);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public async Task DownloadFromCandidatesAsync_FirstFails_FallsThroughToSecond()
    {
        var failing = new FakeModelSource("primary-host", shouldThrow: true);
        var fallback = new FakeModelSource("fallback-host");
        using var dl = new ModelDownloader(new IModelSource[] { failing, fallback });
        var tmp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            var winner = await dl.DownloadFromCandidatesAsync(
                new[] { "https://primary-host/m.bin", "https://fallback-host/m.bin" }, tmp);

            Assert.Equal("fallback-host", winner);
            Assert.Equal(1, failing.DownloadCallCount);
            Assert.Equal(1, fallback.DownloadCallCount);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public async Task DownloadFromCandidatesAsync_AllFail_ThrowsInvalidOperation()
    {
        var src = new FakeModelSource("fakehost", shouldThrow: true);
        using var dl = new ModelDownloader(new[] { src });
        var tmp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dl.DownloadFromCandidatesAsync(
                new[] { "https://fakehost/model.bin" }, tmp));
    }

    // -----------------------------------------------------------------------
    // Cancellation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DownloadFromCandidatesAsync_CancelledToken_ThrowsOperationCancelled()
    {
        var src = new FakeModelSource("fakehost");
        using var dl = new ModelDownloader(new[] { src });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            dl.DownloadFromCandidatesAsync(
                new[] { "https://fakehost/m.bin" }, "/tmp/z", null, cts.Token));
    }

    // -----------------------------------------------------------------------
    // Dispose idempotency
    // -----------------------------------------------------------------------

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var src = new FakeModelSource();
        var dl = new ModelDownloader(new[] { src });
        dl.Dispose();
        var ex = Record.Exception(() => dl.Dispose());
        Assert.Null(ex);
    }

    // -----------------------------------------------------------------------
    // ownsSources — disposes child sources when requested
    // -----------------------------------------------------------------------

    [Fact]
    public void Dispose_WithOwnsSources_DisposesChildren()
    {
        var disposable = new DisposableModelSource();
        var dl = new ModelDownloader(new IModelSource[] { disposable }, ownsSources: true);
        dl.Dispose();
        Assert.True(disposable.Disposed);
    }

    private sealed class DisposableModelSource : IModelSource, IDisposable
    {
        public string Name => "disposable";
        public bool Disposed { get; private set; }

        public Task<bool> IsAvailableAsync(CancellationToken ct = default)
            => Task.FromResult(true);

        public Task DownloadAsync(string url, string localPath,
            IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public void Dispose() => Disposed = true;
    }
}
