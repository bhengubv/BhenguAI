using Bhengu.AI.Hosting;
using Bhengu.AI.Inference;
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

    // ------------------------------------------------------------------
    // DefaultGenerationOptions (not covered in Defaults_AreCorrect above)
    // ------------------------------------------------------------------

    [Fact]
    public void DefaultGenerationOptions_DefaultIsNull()
    {
        var opts = new ButlerOptions();
        Assert.Null(opts.DefaultGenerationOptions);
    }

    [Fact]
    public void DefaultGenerationOptions_CanBeSet()
    {
        var genOpts = new GenerationOptions { MaxTokens = 256, Temperature = 0.5f };
        var opts = new ButlerOptions { DefaultGenerationOptions = genOpts };
        Assert.Same(genOpts, opts.DefaultGenerationOptions);
    }

    // ------------------------------------------------------------------
    // Init-only properties — spot check full round-trip
    // ------------------------------------------------------------------

    [Fact]
    public void InitProperties_AllCanBeOverridden()
    {
        var toolBridge = new FakeToolBridge();
        var observer   = new FakeButlerObserver();
        var genOpts    = new GenerationOptions { MaxTokens = 1024 };

        var opts = new ButlerOptions
        {
            ModelId                  = "Qwen3.6-35B-A3B-Q3",
            ModelPath                = "/data/models/qwen.gguf",
            SystemPrompt             = "Custom prompt.",
            ContextSize              = 8192,
            ThreadCount              = 4,
            WarmOnStart              = false,
            LoopbackPort             = 9090,
            LoopbackToken            = "fixed-tok",
            ToolBridge               = toolBridge,
            Observer                 = observer,
            DefaultGenerationOptions = genOpts,
        };

        Assert.Equal("Qwen3.6-35B-A3B-Q3",   opts.ModelId);
        Assert.Equal("/data/models/qwen.gguf", opts.ModelPath);
        Assert.Equal("Custom prompt.",         opts.SystemPrompt);
        Assert.Equal(8192,                     opts.ContextSize);
        Assert.Equal(4,                        opts.ThreadCount);
        Assert.False(opts.WarmOnStart);
        Assert.Equal(9090,                     opts.LoopbackPort);
        Assert.Equal("fixed-tok",              opts.LoopbackToken);
        Assert.Same(toolBridge,                opts.ToolBridge);
        Assert.Same(observer,                  opts.Observer);
        Assert.Same(genOpts,                   opts.DefaultGenerationOptions);
    }
}
