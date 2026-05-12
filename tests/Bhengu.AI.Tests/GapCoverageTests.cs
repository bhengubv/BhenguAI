// GapCoverageTests.cs
//
// Targeted tests for gaps identified in the structured code audit:
//
//   • StreamAsync — enriched system prompt (device context, persona)
//   • StreamAsync — episodic storage of the full streamed response
//   • PersonaState — TopicWeights + DisfavouredTopics stored & round-trip
//   • InMemoryEpisodicStore — concurrent add/read stress
//   • RagContextBuilder — throwing embedder gracefully falls back to recency
//   • AIService.AgenticChatAsync — maxIter=0 guard
//   • GenerationOptions — default values documented + validated
//   • JsonPersonaStore — concurrent saves are atomic (no corruption)
//   • AIService — caller-supplied system message blocks enrichment
//     but device context still wires up for non-system-message callers

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bhengu.AI.Core;
using Bhengu.AI.Hosting;
using Bhengu.AI.Inference;
using Bhengu.AI.Memory;
using Xunit;

namespace Bhengu.AI.Tests;

// ============================================================================
// StreamAsync — enriched system prompt injected during streaming
// ============================================================================

public sealed class StreamAsyncEnrichmentTests : IDisposable
{
    private readonly string _modelPath = Path.GetTempFileName();

    public void Dispose()
    {
        try { File.Delete(_modelPath); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task StreamAsync_WithDeviceContext_InjectsContextIntoSystemPrompt()
    {
        var ctx = new FakeDeviceContext { ActiveAppId = "tgn.panik", BatteryLevel = 0.5f };
        var gen = new CapturingChatGenerator("streamed");

        await using var svc = new AIService(
            new AIOptions
            {
                ModelPath     = _modelPath,
                WarmOnStart   = false,
                DeviceContext  = ctx,
                SystemPrompt  = "You are B!",
            },
            generatorFactory: _ => gen);

        // Drain the stream.
        var chunks = new List<string>();
        await foreach (var c in svc.StreamAsync(new[] { new ChatMessage("user", "hello") }))
            chunks.Add(c);

        Assert.Single(gen.CapturedSystemMessages);
        var sys = gen.CapturedSystemMessages[0]!;
        Assert.Contains("tgn.panik", sys);
        Assert.Contains("Battery", sys);
    }

    [Fact]
    public async Task StreamAsync_WithPersona_InjectsPersonaHint()
    {
        var personaStore = new InMemoryPersonaStore();
        var persona = await personaStore.LoadAsync("stream-user");
        persona.Verbosity = "detailed";
        await personaStore.SaveAsync(persona);

        var gen = new CapturingChatGenerator("streamed reply");

        await using var svc = new AIService(
            new AIOptions
            {
                ModelPath     = _modelPath,
                WarmOnStart   = false,
                PersonaStore  = personaStore,
                PersonaUserId = "stream-user",
            },
            generatorFactory: _ => gen);

        await foreach (var _ in svc.StreamAsync(new[] { new ChatMessage("user", "hi") })) { }

        Assert.Single(gen.CapturedSystemMessages);
        var sys = gen.CapturedSystemMessages[0]!;
        Assert.Contains("detailed", sys, StringComparison.OrdinalIgnoreCase);
    }
}

// ============================================================================
// StreamAsync — episodic storage of the full accumulated response
// ============================================================================

public sealed class StreamAsyncEpisodicStorageTests : IDisposable
{
    private readonly string _modelPath = Path.GetTempFileName();

    public void Dispose()
    {
        try { File.Delete(_modelPath); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task StreamAsync_StoresFullResponseInEpisodicMemory()
    {
        var memory = new InMemoryEpisodicStore();
        var gen    = new FakeChatGenerator("stream reply", new[] { "stream", " ", "reply" });

        await using var svc = new AIService(
            new AIOptions
            {
                ModelPath      = _modelPath,
                WarmOnStart    = false,
                EpisodicMemory = memory,
            },
            generatorFactory: _ => gen);

        // Drain the full stream to ensure the episodic store fires.
        await foreach (var _ in svc.StreamAsync(new[] { new ChatMessage("user", "store me") })) { }

        // Give the fire-and-forget a moment.
        await Task.Delay(100);

        Assert.Equal(1, await memory.CountAsync());
        var recent = await memory.GetRecentAsync(1);
        Assert.Equal("stream reply", recent[0].AssistantText);
        Assert.Equal("store me",     recent[0].UserText);
    }
}

// ============================================================================
// PersonaState — TopicWeights + DisfavouredTopics
// ============================================================================

public sealed class PersonaStateTopicTests
{
    [Fact]
    public void TopicWeights_CanBeSetAndRead()
    {
        var p = new PersonaState { UserId = "u1" };
        p.TopicWeights["finance"]  = 0.8f;
        p.TopicWeights["sport"]    = 0.3f;

        Assert.Equal(0.8f, p.TopicWeights["finance"], precision: 5);
        Assert.Equal(0.3f, p.TopicWeights["sport"],   precision: 5);
    }

    [Fact]
    public void DisfavouredTopics_CanBeAddedAndQueried()
    {
        var p = new PersonaState { UserId = "u2" };
        p.DisfavouredTopics.Add("politics");
        p.DisfavouredTopics.Add("explicit");

        Assert.Contains("politics", p.DisfavouredTopics);
        Assert.Contains("explicit", p.DisfavouredTopics);
        Assert.DoesNotContain("sport", p.DisfavouredTopics);
    }

    [Fact]
    public void ToSystemPromptHint_TopicWeights_NotYetSurfaced()
    {
        // TopicWeights and DisfavouredTopics are stored for future LoRA
        // fine-tuning (Phase 2.1) but are NOT injected into the prompt hint
        // in the current release. This test documents that contract.
        var p = new PersonaState
        {
            Verbosity = "balanced", // default → no hint
        };
        p.TopicWeights["crypto"] = 1.0f;
        p.DisfavouredTopics.Add("spam");

        Assert.Equal(string.Empty, p.ToSystemPromptHint());
    }

    [Fact]
    public async Task InMemoryPersonaStore_TopicWeights_RoundTrip()
    {
        var store = new InMemoryPersonaStore();
        var p = await store.LoadAsync("wt-user");
        p.TopicWeights["crypto"] = 2.5f;
        p.DisfavouredTopics.Add("ads");
        await store.SaveAsync(p);

        var loaded = await store.LoadAsync("wt-user");
        Assert.Equal(2.5f, loaded.TopicWeights["crypto"], precision: 5);
        Assert.Contains("ads", loaded.DisfavouredTopics);
    }

    [Fact]
    public async Task JsonPersonaStore_TopicWeights_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "persona_topic_" + Guid.NewGuid());
        try
        {
            var store = new JsonPersonaStore(dir);
            var p = new PersonaState
            {
                UserId    = "jsonwt",
                Verbosity = "brief",
            };
            p.TopicWeights["ai"]   = 3.0f;
            p.TopicWeights["tech"] = 1.5f;
            p.DisfavouredTopics.Add("sports");
            await store.SaveAsync(p);

            var loaded = await store.LoadAsync("jsonwt");
            Assert.Equal(3.0f, loaded.TopicWeights["ai"],   precision: 5);
            Assert.Equal(1.5f, loaded.TopicWeights["tech"], precision: 5);
            Assert.Contains("sports", loaded.DisfavouredTopics);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }
}

// ============================================================================
// InMemoryEpisodicStore — concurrent stress
// ============================================================================

public sealed class EpisodicStoreConcurrencyTests
{
    [Fact]
    public async Task InMemoryEpisodicStore_ConcurrentAdds_CountIsConsistent()
    {
        const int threadCount = 10;
        const int entriesPerThread = 50;
        var store = new InMemoryEpisodicStore(maxEntries: 5000);

        var tasks = Enumerable.Range(0, threadCount).Select(t =>
            Task.Run(async () =>
            {
                for (int i = 0; i < entriesPerThread; i++)
                {
                    await store.AddAsync(new EpisodicMemoryEntry
                    {
                        UserText      = $"thread {t} q {i}",
                        AssistantText = $"thread {t} a {i}",
                    });
                }
            })).ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(threadCount * entriesPerThread, await store.CountAsync());
    }

    [Fact]
    public async Task InMemoryEpisodicStore_ConcurrentAddAndRead_NoException()
    {
        var store = new InMemoryEpisodicStore(maxEntries: 1000);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Writers
        var writes = Enumerable.Range(0, 5).Select(i =>
            Task.Run(async () =>
            {
                for (int j = 0; j < 100 && !cts.IsCancellationRequested; j++)
                    await store.AddAsync(new EpisodicMemoryEntry
                    {
                        UserText = $"w{i}q{j}", AssistantText = $"w{i}a{j}",
                    });
            })).ToArray();

        // Readers interleaved.
        var reads = Enumerable.Range(0, 5).Select(_ =>
            Task.Run(async () =>
            {
                for (int j = 0; j < 100 && !cts.IsCancellationRequested; j++)
                    await store.GetRecentAsync(10);
            })).ToArray();

        var ex = await Record.ExceptionAsync(() => Task.WhenAll(writes.Concat(reads)));
        Assert.Null(ex);
    }

    [Fact]
    public async Task InMemoryEpisodicStore_CapacityUnderConcurrency_NeverExceedsMax()
    {
        const int max = 100;
        var store = new InMemoryEpisodicStore(maxEntries: max);

        var tasks = Enumerable.Range(0, 20).Select(t =>
            Task.Run(async () =>
            {
                for (int i = 0; i < 20; i++)
                    await store.AddAsync(new EpisodicMemoryEntry
                    {
                        UserText = $"u{t}{i}", AssistantText = "a",
                    });
            })).ToArray();

        await Task.WhenAll(tasks);

        Assert.True(await store.CountAsync() <= max);
    }
}

// ============================================================================
// RagContextBuilder — throwing embedder falls back to recency
// ============================================================================

public sealed class RagContextBuilderEmbedderResilienceTests
{
    [Fact]
    public async Task BuildContextAsync_EmbedderThrows_FallsBackToRecency()
    {
        var store = new InMemoryEpisodicStore();
        await store.AddAsync(new EpisodicMemoryEntry
        {
            UserText      = "fallback question",
            AssistantText = "fallback answer",
        });

        // Embedder that always throws — RAG must survive and use recency fallback.
        var builder = new RagContextBuilder(store, embedder: new ThrowingEmbedder(), topK: 3);
        var result  = await builder.BuildContextAsync("any question");

        // Should still return content from the recency path.
        Assert.NotEmpty(result);
        Assert.Contains("fallback question", result);
    }

    /// <summary>ITextEmbedder that always throws — tests resilience.</summary>
    private sealed class ThrowingEmbedder : Bhengu.AI.Embeddings.ITextEmbedder
    {
        public Task<float[]> GenerateAsync(string text, CancellationToken ct = default)
            => throw new InvalidOperationException("embedding service offline");
    }
}

// ============================================================================
// AIService.AgenticChatAsync — maxIter guard
// ============================================================================

public sealed class AgenticMaxIterGuardTests : IDisposable
{
    private readonly string _modelPath = Path.GetTempFileName();

    public void Dispose()
    {
        try { File.Delete(_modelPath); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task AgenticChatAsync_MaxIterZero_RunsAtLeastOnce()
    {
        // Math.Max(1, 0) = 1 — the service must complete one iteration.
        var gen = new FakeChatGenerator("one iteration");

        await using var svc = new AIService(
            new AIOptions
            {
                ModelPath            = _modelPath,
                WarmOnStart          = false,
                AgenticMaxIterations = 0, // effectively 1 after Math.Max guard
            },
            generatorFactory: _ => gen);

        var result = await svc.AgenticChatAsync("test zero max");

        Assert.Equal("one iteration", result);
        Assert.Equal(1, gen.GenerateCallCount);
    }

    [Fact]
    public async Task AgenticChatAsync_NegativeMaxIter_RunsAtLeastOnce()
    {
        var gen = new FakeChatGenerator("negative guard");

        await using var svc = new AIService(
            new AIOptions
            {
                ModelPath            = _modelPath,
                WarmOnStart          = false,
                AgenticMaxIterations = -5, // Math.Max(1, -5) = 1
            },
            generatorFactory: _ => gen);

        var result = await svc.AgenticChatAsync("negative test");
        Assert.Equal("negative guard", result);
    }
}

// ============================================================================
// GenerationOptions — default values documented
// ============================================================================

public sealed class GenerationOptionsDefaultsTests
{
    [Fact]
    public void Defaults_MaxTokens_Is512()
    {
        var opts = new GenerationOptions();
        Assert.Equal(512, opts.MaxTokens);
    }

    [Fact]
    public void Defaults_Temperature_Is0_7()
    {
        var opts = new GenerationOptions();
        Assert.Equal(0.7f, opts.Temperature, precision: 5);
    }

    [Fact]
    public void Defaults_TopP_Is0_9()
    {
        var opts = new GenerationOptions();
        Assert.Equal(0.9f, opts.TopP, precision: 5);
    }

    [Fact]
    public void Defaults_TopK_Is40()
    {
        var opts = new GenerationOptions();
        Assert.Equal(40, opts.TopK);
    }

    [Fact]
    public void Defaults_Seed_IsNull()
    {
        var opts = new GenerationOptions();
        Assert.Null(opts.Seed);
    }

    [Fact]
    public void Defaults_StopSequences_IsNull()
    {
        var opts = new GenerationOptions();
        Assert.Null(opts.StopSequences);
    }

    [Fact]
    public void With_CanOverrideAllProperties()
    {
        // Init-only record-like overrides.
        var opts = new GenerationOptions
        {
            MaxTokens     = 256,
            Temperature   = 0.0f,  // greedy
            TopP          = 1.0f,
            TopK          = 0,
            Seed          = 42,
            StopSequences = new[] { "<|end|>", "<|im_end|>" },
        };
        Assert.Equal(256,             opts.MaxTokens);
        Assert.Equal(0.0f,            opts.Temperature, precision: 5);
        Assert.Equal(1.0f,            opts.TopP,        precision: 5);
        Assert.Equal(0,               opts.TopK);
        Assert.Equal(42,              opts.Seed);
        Assert.Equal(2,               opts.StopSequences!.Length);
        Assert.Contains("<|end|>",    opts.StopSequences);
    }
}

// ============================================================================
// JsonPersonaStore — concurrent saves are atomic (no corruption)
// ============================================================================

public sealed class JsonPersonaStoreConcurrencyTests : IDisposable
{
    private readonly string _dir;

    public JsonPersonaStoreConcurrencyTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "persona_conc_" + Guid.NewGuid());
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task ConcurrentSaves_SameUser_LastWriteWins_NoCorruption()
    {
        var store = new JsonPersonaStore(_dir);

        // Ten concurrent saves with different verbosity values.
        var tasks = Enumerable.Range(0, 10).Select(i =>
            Task.Run(async () =>
            {
                var p = new PersonaState
                {
                    UserId    = "shared-user",
                    Verbosity = i % 2 == 0 ? "brief" : "detailed",
                };
                await store.SaveAsync(p);
            })).ToArray();

        await Task.WhenAll(tasks);

        // After all saves, the file must be valid JSON and loadable.
        var loaded = await store.LoadAsync("shared-user");
        Assert.Equal("shared-user", loaded.UserId);
        Assert.True(loaded.Verbosity == "brief" || loaded.Verbosity == "detailed",
            $"Unexpected verbosity '{loaded.Verbosity}' after concurrent saves.");
    }

    [Fact]
    public async Task ConcurrentSaves_DifferentUsers_AllPresent()
    {
        var store = new JsonPersonaStore(_dir);
        const int userCount = 20;

        var tasks = Enumerable.Range(0, userCount).Select(i =>
            Task.Run(() => store.SaveAsync(new PersonaState
            {
                UserId    = $"user{i}",
                Verbosity = "brief",
            }))).ToArray();

        await Task.WhenAll(tasks);

        // All 20 different user files should be loadable.
        for (int i = 0; i < userCount; i++)
        {
            var loaded = await store.LoadAsync($"user{i}");
            Assert.Equal($"user{i}", loaded.UserId);
        }
    }
}
