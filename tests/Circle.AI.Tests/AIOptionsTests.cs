using Circle.AI.Core;
using Circle.AI.Hosting;
using Circle.AI.Inference;
using Circle.AI.Memory;
using Xunit;

namespace Circle.AI.Tests;

public sealed class AIOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var opts = new AIOptions();

        Assert.Equal("Qwen3.6-35B-A3B-Q3", opts.ModelId);
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
        var token = AIOptions.GenerateRandomToken();
        Assert.Equal(44, token.Length);
    }

    [Fact]
    public void GenerateRandomToken_IsUnique()
    {
        var t1 = AIOptions.GenerateRandomToken();
        var t2 = AIOptions.GenerateRandomToken();
        Assert.NotEqual(t1, t2);
    }

    [Fact]
    public void GenerateRandomToken_IsValidBase64()
    {
        var token = AIOptions.GenerateRandomToken();
        var bytes = Convert.FromBase64String(token);
        Assert.Equal(32, bytes.Length);
    }

    // ------------------------------------------------------------------
    // DefaultGenerationOptions (not covered in Defaults_AreCorrect above)
    // ------------------------------------------------------------------

    [Fact]
    public void DefaultGenerationOptions_DefaultIsNull()
    {
        var opts = new AIOptions();
        Assert.Null(opts.DefaultGenerationOptions);
    }

    [Fact]
    public void DefaultGenerationOptions_CanBeSet()
    {
        var genOpts = new GenerationOptions { MaxTokens = 256, Temperature = 0.5f };
        var opts = new AIOptions { DefaultGenerationOptions = genOpts };
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

        var opts = new AIOptions
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

    // ------------------------------------------------------------------
    // v2.0 properties — defaults
    // ------------------------------------------------------------------

    [Fact]
    public void V2_Defaults_AreCorrect()
    {
        var opts = new AIOptions();

        // Sensorium
        Assert.Null(opts.DeviceContext);

        // Memory / RAG
        Assert.Null(opts.EpisodicMemory);
        Assert.Null(opts.RagBuilder);
        Assert.Equal(5, opts.RagTopK);

        // Persona
        Assert.Null(opts.PersonaStore);
        Assert.Equal("default", opts.PersonaUserId);

        // Feedback
        Assert.Null(opts.FeedbackStore);

        // Agentic loop
        Assert.Equal(5, opts.AgenticMaxIterations);
    }

    // ------------------------------------------------------------------
    // v2.0 properties — init-only overrides
    // ------------------------------------------------------------------

    [Fact]
    public void V2_InitProperties_AllCanBeOverridden()
    {
        var episodicMemory  = new InMemoryEpisodicStore();
        var ragBuilder      = new RagContextBuilder(episodicMemory);
        var personaStore    = new InMemoryPersonaStore();
        var feedbackStore   = new InMemoryFeedbackStore();
        var deviceCtx       = new FakeDeviceContext { ActiveAppId = "tgn.butler" };

        var opts = new AIOptions
        {
            DeviceContext        = deviceCtx,
            EpisodicMemory       = episodicMemory,
            RagBuilder           = ragBuilder,
            RagTopK              = 10,
            PersonaStore         = personaStore,
            PersonaUserId        = "user-123",
            FeedbackStore        = feedbackStore,
            AgenticMaxIterations = 3,
        };

        Assert.Same(deviceCtx,      opts.DeviceContext);
        Assert.Same(episodicMemory, opts.EpisodicMemory);
        Assert.Same(ragBuilder,     opts.RagBuilder);
        Assert.Equal(10,            opts.RagTopK);
        Assert.Same(personaStore,   opts.PersonaStore);
        Assert.Equal("user-123",    opts.PersonaUserId);
        Assert.Same(feedbackStore,  opts.FeedbackStore);
        Assert.Equal(3,             opts.AgenticMaxIterations);
    }
}
