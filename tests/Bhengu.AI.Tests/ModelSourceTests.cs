using System;
using System.Threading.Tasks;
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
