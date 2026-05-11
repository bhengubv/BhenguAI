using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bhengu.AI.Core;
using Bhengu.AI.Core.Sources;
using Xunit;

namespace Bhengu.AI.Tests;

/// <summary>
/// Unit tests for <see cref="HuggingFaceSource"/> and <see cref="ModelScopeSource"/>.
/// Only exercises argument validation and dispose guards — no network I/O.
/// </summary>
public sealed class ModelSourceTests
{
    // =========================================================================
    // HuggingFaceSource
    // =========================================================================

    [Fact]
    public void HuggingFaceSource_Name_IsHuggingFace()
    {
        using var src = new HuggingFaceSource();
        Assert.Equal("HuggingFace", src.Name);
    }

    [Fact]
    public async Task HuggingFaceSource_DownloadAsync_NullUrl_Throws()
    {
        using var src = new HuggingFaceSource();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            src.DownloadAsync(null!, "/tmp/model.bin"));
    }

    [Fact]
    public async Task HuggingFaceSource_DownloadAsync_WhitespaceUrl_Throws()
    {
        using var src = new HuggingFaceSource();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            src.DownloadAsync("   ", "/tmp/model.bin"));
    }

    [Fact]
    public async Task HuggingFaceSource_DownloadAsync_NullLocalPath_Throws()
    {
        using var src = new HuggingFaceSource();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            src.DownloadAsync("https://huggingface.co/file.bin", null!));
    }

    [Fact]
    public async Task HuggingFaceSource_DownloadAsync_WrongHost_ThrowsArgumentException()
    {
        using var src = new HuggingFaceSource();
        // modelscope.cn is not huggingface.co — must reject
        await Assert.ThrowsAsync<ArgumentException>(() =>
            src.DownloadAsync("https://modelscope.cn/file.bin", "/tmp/x"));
    }

    [Fact]
    public async Task HuggingFaceSource_DownloadAsync_AfterDispose_Throws()
    {
        var src = new HuggingFaceSource();
        src.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            src.DownloadAsync("https://huggingface.co/file.bin", "/tmp/x"));
    }

    [Fact]
    public async Task HuggingFaceSource_IsAvailableAsync_AfterDispose_ReturnsFalse()
    {
        var src = new HuggingFaceSource();
        src.Dispose();
        var result = await src.IsAvailableAsync();
        Assert.False(result);
    }

    [Fact]
    public void HuggingFaceSource_Dispose_IsIdempotent()
    {
        var src = new HuggingFaceSource();
        src.Dispose();
        var ex = Record.Exception(() => src.Dispose());
        Assert.Null(ex);
    }

    // =========================================================================
    // ModelScopeSource
    // =========================================================================

    [Fact]
    public void ModelScopeSource_Name_IsModelScope()
    {
        using var src = new ModelScopeSource();
        Assert.Equal("ModelScope", src.Name);
    }

    [Fact]
    public async Task ModelScopeSource_DownloadAsync_NullUrl_Throws()
    {
        using var src = new ModelScopeSource();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            src.DownloadAsync(null!, "/tmp/model.bin"));
    }

    [Fact]
    public async Task ModelScopeSource_DownloadAsync_WhitespaceUrl_Throws()
    {
        using var src = new ModelScopeSource();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            src.DownloadAsync("   ", "/tmp/model.bin"));
    }

    [Fact]
    public async Task ModelScopeSource_DownloadAsync_NullLocalPath_Throws()
    {
        using var src = new ModelScopeSource();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            src.DownloadAsync("https://modelscope.cn/file.bin", null!));
    }

    [Fact]
    public async Task ModelScopeSource_DownloadAsync_WrongHost_ThrowsArgumentException()
    {
        using var src = new ModelScopeSource();
        // huggingface.co is not modelscope.cn — must reject
        await Assert.ThrowsAsync<ArgumentException>(() =>
            src.DownloadAsync("https://huggingface.co/file.bin", "/tmp/x"));
    }

    [Fact]
    public async Task ModelScopeSource_DownloadAsync_AfterDispose_Throws()
    {
        var src = new ModelScopeSource();
        src.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            src.DownloadAsync("https://modelscope.cn/file.bin", "/tmp/x"));
    }

    [Fact]
    public async Task ModelScopeSource_IsAvailableAsync_AfterDispose_ReturnsFalse()
    {
        var src = new ModelScopeSource();
        src.Dispose();
        var result = await src.IsAvailableAsync();
        Assert.False(result);
    }

    [Fact]
    public void ModelScopeSource_Dispose_IsIdempotent()
    {
        var src = new ModelScopeSource();
        src.Dispose();
        var ex = Record.Exception(() => src.Dispose());
        Assert.Null(ex);
    }

    // =========================================================================
    // Cross-source: wrong host rejection is symmetric
    // =========================================================================

    [Theory]
    [InlineData("https://evil.com/file.bin")]
    [InlineData("https://cdn.example.net/model.gguf")]
    [InlineData("https://s3.amazonaws.com/bucket/model.bin")]
    public async Task HuggingFaceSource_RejectsNonHuggingFaceUrls(string url)
    {
        using var src = new HuggingFaceSource();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            src.DownloadAsync(url, "/tmp/x"));
    }

    [Theory]
    [InlineData("https://evil.com/file.bin")]
    [InlineData("https://cdn.example.net/model.gguf")]
    [InlineData("https://huggingface.co/model.bin")]
    public async Task ModelScopeSource_RejectsNonModelScopeUrls(string url)
    {
        using var src = new ModelScopeSource();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            src.DownloadAsync(url, "/tmp/x"));
    }
}

// =========================================================================
// ownsClient semantics — injected HttpClient must NOT be disposed by source
// =========================================================================

public sealed class ModelSourceOwnershipTests
{
    /// <summary>
    /// Message handler that tracks whether <see cref="Dispose"/> was called.
    /// </summary>
    private sealed class TrackingHandler : HttpMessageHandler
    {
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => throw new NotImplementedException("not used in disposal tests");

        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposed = true;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public void HuggingFaceSource_InjectedClient_IsNotDisposedOnSourceDispose()
    {
        // When an HttpClient is injected (ownsClient=false) the source must
        // never dispose it — the caller retains ownership.
        var handler = new TrackingHandler();
        using var http = new HttpClient(handler, disposeHandler: false);
        var src = new HuggingFaceSource(http);
        src.Dispose();
        Assert.False(handler.Disposed,
            "HuggingFaceSource must not dispose an injected HttpClient.");
    }

    [Fact]
    public void ModelScopeSource_InjectedClient_IsNotDisposedOnSourceDispose()
    {
        var handler = new TrackingHandler();
        using var http = new HttpClient(handler, disposeHandler: false);
        var src = new ModelScopeSource(http);
        src.Dispose();
        Assert.False(handler.Disposed,
            "ModelScopeSource must not dispose an injected HttpClient.");
    }

    [Fact]
    public async Task HuggingFaceSource_OwnedClient_IsAvailableAsync_AfterDispose_ReturnsFalse()
    {
        // Sanity check that the _disposed guard short-circuits IsAvailableAsync
        // when the source created and owns its own client.
        var src = new HuggingFaceSource();   // ownsClient = true
        src.Dispose();
        Assert.False(await src.IsAvailableAsync());
    }

    [Fact]
    public async Task ModelScopeSource_OwnedClient_IsAvailableAsync_AfterDispose_ReturnsFalse()
    {
        var src = new ModelScopeSource();    // ownsClient = true
        src.Dispose();
        Assert.False(await src.IsAvailableAsync());
    }
}

// =========================================================================
// SourceDownloadHelper behaviour — tested via HuggingFaceSource with a
// fake HttpMessageHandler so we exercise the real download pipeline without
// any network I/O.
// =========================================================================

public sealed class SourceDownloadHelperBehaviourTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "sdh_" + Guid.NewGuid().ToString("N"));

    public SourceDownloadHelperBehaviourTests()
        => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private string TempFile(string name) => Path.Combine(_tempDir, name);

    // Synchronous IProgress<T> so callbacks fire inline — no Task.Delay needed.
    private sealed class SyncProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    // Handler that returns fixed content with a configurable status code.
    private sealed class ContentHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly byte[] _body;

        public ContentHandler(byte[] body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            LastRequest = request;
            var content = new ByteArrayContent(_body);
            content.Headers.ContentLength = _body.Length;
            return Task.FromResult(new HttpResponseMessage(_status) { Content = content });
        }
    }

    // ------------------------------------------------------------------

    [Fact]
    public async Task FreshDownload_WritesAllBytesToFile()
    {
        var payload = Enumerable.Range(1, 20).Select(i => (byte)i).ToArray();
        var path = TempFile("model.bin");

        using var handler = new ContentHandler(payload);
        using var src = new HuggingFaceSource(new HttpClient(handler));

        await src.DownloadAsync("https://huggingface.co/model.bin", path);

        Assert.Equal(payload, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Resume_206PartialContent_AppendsToExistingFile()
    {
        var initial = new byte[] { 10, 20, 30 };
        var extra   = new byte[] { 40, 50, 60 };
        var path    = TempFile("resume.bin");
        await File.WriteAllBytesAsync(path, initial);

        // Server honours the Range header and returns 206.
        using var handler = new ContentHandler(extra, HttpStatusCode.PartialContent);
        using var src = new HuggingFaceSource(new HttpClient(handler));

        await src.DownloadAsync("https://huggingface.co/resume.bin", path);

        var expected = initial.Concat(extra).ToArray();
        Assert.Equal(expected, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Resume_ServerReturns200_OverwritesFile()
    {
        // Partial file exists, but server ignores Range and returns 200 OK.
        // SourceDownloadHelper must restart from scratch.
        var stale = new byte[] { 0xFF, 0xFF };
        var fresh = new byte[] { 1, 2, 3, 4, 5 };
        var path  = TempFile("overwrite.bin");
        await File.WriteAllBytesAsync(path, stale);

        using var handler = new ContentHandler(fresh, HttpStatusCode.OK);
        using var src = new HuggingFaceSource(new HttpClient(handler));

        await src.DownloadAsync("https://huggingface.co/overwrite.bin", path);

        Assert.Equal(fresh, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Progress_IsCalled_WithFinalByteCount()
    {
        var payload = Enumerable.Range(0, 50).Select(i => (byte)i).ToArray();
        var path    = TempFile("progress.bin");

        var reports = new List<DownloadProgress>();
        var prog    = new SyncProgress<DownloadProgress>(r => reports.Add(r));

        using var handler = new ContentHandler(payload);
        using var src = new HuggingFaceSource(new HttpClient(handler));

        await src.DownloadAsync("https://huggingface.co/progress.bin", path, prog);

        // At least one report must have been fired (triggered when bytesRead == totalBytes).
        Assert.NotEmpty(reports);
        var last = reports.Last();
        Assert.Equal(payload.Length, last.BytesReceived);
        Assert.Equal(payload.Length, last.TotalBytes);
    }

    [Fact]
    public async Task Progress_FileName_MatchesLocalPathBaseName()
    {
        var payload = new byte[] { 1, 2, 3 };
        var path    = TempFile("my_model.gguf");

        DownloadProgress? report = null;
        var prog = new SyncProgress<DownloadProgress>(r => report = r);

        using var handler = new ContentHandler(payload);
        using var src = new HuggingFaceSource(new HttpClient(handler));

        await src.DownloadAsync("https://huggingface.co/my_model.gguf", path, prog);

        Assert.NotNull(report);
        Assert.Equal("my_model.gguf", report!.FileName);
    }

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceledException()
    {
        var payload = new byte[4096];
        var path    = TempFile("cancel.bin");

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // pre-cancel

        using var handler = new ContentHandler(payload);
        using var src = new HuggingFaceSource(new HttpClient(handler));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            src.DownloadAsync("https://huggingface.co/cancel.bin", path, ct: cts.Token));
    }

    [Fact]
    public async Task Resume_SendsRangeHeader_WhenPartialFileExists()
    {
        var existing = new byte[] { 1, 2 };
        var path     = TempFile("rangecheck.bin");
        await File.WriteAllBytesAsync(path, existing);

        using var handler = new ContentHandler(new byte[] { 3, 4 }, HttpStatusCode.PartialContent);
        using var src = new HuggingFaceSource(new HttpClient(handler));

        await src.DownloadAsync("https://huggingface.co/rangecheck.bin", path);

        // The helper must have sent a Range: bytes=2- header.
        Assert.NotNull(handler.LastRequest?.Headers.Range);
        Assert.Equal(2, handler.LastRequest!.Headers.Range!.Ranges.Single().From);
        Assert.Null(handler.LastRequest.Headers.Range.Ranges.Single().To); // open-ended
    }

    [Fact]
    public async Task FreshDownload_DoesNotSendRangeHeader()
    {
        // No existing file → no Range header on the request.
        var path = TempFile("norange.bin");

        using var handler = new ContentHandler(new byte[] { 7, 8, 9 });
        using var src = new HuggingFaceSource(new HttpClient(handler));

        await src.DownloadAsync("https://huggingface.co/norange.bin", path);

        Assert.Null(handler.LastRequest?.Headers.Range);
    }
}
