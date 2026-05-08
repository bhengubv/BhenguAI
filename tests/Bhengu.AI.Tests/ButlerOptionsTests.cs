using Bhengu.AI.Hosting;
using Xunit;

namespace Bhengu.AI.Tests;

public sealed class ButlerOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var opts = new ButlerOptions();

        Assert.Equal("Qwen3-14B-Q4", opts.ModelId);
        Assert.Null(opts.ModelPath);
        Assert.Contains("B!", opts.SystemPrompt);
        Assert.Equal(4096, opts.ContextSize);
        Assert.Null(opts.ThreadCount);
        Assert.True(opts.WarmOnStart);
        Assert.Equal(0, opts.LoopbackPort);
        Assert.Null(opts.LoopbackToken);
        Assert.Null(opts.ToolBridge);
        Assert.Null(opts.Observer);
    }

    [Fact]
    public void GenerateRandomToken_Is44CharBase64()
    {
        // 32 bytes → 44 chars in base64
        var token = ButlerOptions.GenerateRandomToken();
        Assert.Equal(44, token.Length);
    }

    [Fact]
    public void GenerateRandomToken_IsUnique()
    {
        var t1 = ButlerOptions.GenerateRandomToken();
        var t2 = ButlerOptions.GenerateRandomToken();
        Assert.NotEqual(t1, t2);
    }

    [Fact]
    public void GenerateRandomToken_IsValidBase64()
    {
        var token = ButlerOptions.GenerateRandomToken();
        var bytes = Convert.FromBase64String(token);
        Assert.Equal(32, bytes.Length);
    }
}
