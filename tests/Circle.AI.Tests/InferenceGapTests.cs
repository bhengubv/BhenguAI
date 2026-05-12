using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Circle.AI.Inference;
using Xunit;

namespace Circle.AI.Tests;

/// <summary>
/// Smoke tests for Inference project gaps (2, 5, 7).
/// These tests verify compilation and structure — they cannot call the native
/// P/Invokes without a native library present.
/// </summary>
public sealed class InferenceGapTests
{
    // ── Gap 2: KV cache P/Invokes ─────────────────────────────────────────

    [Fact]
    public void LlamaCppInterop_HasSessionPInvokes()
    {
        var t = typeof(LlamaCppInterop);

        Assert.NotNull(t.GetMethod("llama_state_get_size",
            BindingFlags.Public | BindingFlags.Static));

        Assert.NotNull(t.GetMethod("llama_state_get_data",
            BindingFlags.Public | BindingFlags.Static));

        Assert.NotNull(t.GetMethod("llama_state_set_data",
            BindingFlags.Public | BindingFlags.Static));

        Assert.NotNull(t.GetMethod("llama_state_save_file",
            BindingFlags.Public | BindingFlags.Static));

        Assert.NotNull(t.GetMethod("llama_state_load_file",
            BindingFlags.Public | BindingFlags.Static));
    }

    [Fact]
    public void LlamaCppInterop_HasSaveAndLoadSessionHelpers()
    {
        var t = typeof(LlamaCppInterop);

        Assert.NotNull(t.GetMethod("SaveSession",
            BindingFlags.Public | BindingFlags.Static));

        Assert.NotNull(t.GetMethod("LoadSession",
            BindingFlags.Public | BindingFlags.Static));
    }

    // ── Gap 7: Vision P/Invokes ───────────────────────────────────────────

    [Fact]
    public void LlamaCppInterop_HasLlavaPInvokes()
    {
        var t = typeof(LlamaCppInterop);

        Assert.NotNull(t.GetMethod("llava_image_embed_make_with_bytes",
            BindingFlags.Public | BindingFlags.Static));

        Assert.NotNull(t.GetMethod("llava_image_embed_free",
            BindingFlags.Public | BindingFlags.Static));

        Assert.NotNull(t.GetMethod("llava_eval_image_embed",
            BindingFlags.Public | BindingFlags.Static));
    }

    // ── Gap 7: VisionInput ────────────────────────────────────────────────

    [Fact]
    public void VisionInput_Properties_RoundTrip()
    {
        var img = new byte[] { 1, 2, 3 };
        var vi = new VisionInput { ImageBytes = img, MimeType = "image/jpeg" };

        Assert.Equal(img, vi.ImageBytes);
        Assert.Equal("image/jpeg", vi.MimeType);
    }

    [Fact]
    public void VisionInput_MimeType_IsOptional()
    {
        var vi = new VisionInput { ImageBytes = new byte[] { 0xFF } };
        Assert.Null(vi.MimeType);
    }

    // ── Gap 5: NativeLibraryResolver ─────────────────────────────────────

    [Fact]
    public void NativeLibraryResolver_EnsureRegistered_DoesNotThrow()
    {
        // Calling twice should be idempotent
        var ex1 = Record.Exception(NativeLibraryResolver.EnsureRegistered);
        var ex2 = Record.Exception(NativeLibraryResolver.EnsureRegistered);

        Assert.Null(ex1);
        Assert.Null(ex2);
    }
}

// ── ModelSelectorTests ────────────────────────────────────────────────────────

public sealed class ModelSelectorTests
{
    private static long GB(int gb) => (long)gb * 1024 * 1024 * 1024;

    [Fact]
    public void DefaultTiers_IsNonEmpty()
    {
        Assert.NotEmpty(ModelSelector.DefaultTiers);
    }

    [Fact]
    public void Select_6GB_ReturnsMidRangeTier()
    {
        // 6 GB fits "Qwen3-4B-Q4" (4 GB) but not "Qwen3.6-35B-A3B-Q3" (8 GB)
        var tier = ModelSelector.Select(GB(6));
        Assert.Equal("Qwen3-4B-Q4", tier.ModelId);
    }

    [Fact]
    public void Select_12GB_ReturnsFlagshipTier()
    {
        // 12 GB fits "Qwen3.6-35B-A3B-Q3" (8 GB) but not "Qwen3-30B-A3B-Q4" (16 GB)
        var tier = ModelSelector.Select(GB(12));
        Assert.Equal("Qwen3.6-35B-A3B-Q3", tier.ModelId);
    }

    [Fact]
    public void Select_64GB_ReturnsServerTier()
    {
        // 64 GB comfortably fits the top tier (48 GB)
        var tier = ModelSelector.Select(GB(64));
        Assert.Equal("Qwen3-235B-A22B-Q2", tier.ModelId);
    }

    [Fact]
    public void Select_512MB_FallsBackToLowestTier()
    {
        // 512 MB is below every tier's minimum; lowest tier must be returned
        var tier = ModelSelector.Select(512L * 1024 * 1024);
        // Lowest tier by MinRamBytes
        var expected = ModelSelector.DefaultTiers
            .OrderBy(t => t.MinRamBytes)
            .First();
        Assert.Equal(expected.ModelId, tier.ModelId);
    }
}

// ── ContextWindowBudgetManagerTests ──────────────────────────────────────────

public sealed class ContextWindowBudgetManagerTests
{
    [Fact]
    public void RemainingTokens_DecreasesAfterRecordExchange()
    {
        var mgr = new ContextWindowBudgetManager(contextSize: 4096);
        mgr.RecordExchange(promptTokens: 200, completionTokens: 100);

        Assert.Equal(4096 - 300, mgr.RemainingTokens);
        Assert.Equal(300, mgr.UsedTokens);
    }

    [Fact]
    public void ShouldEvict_TrueWhenFillRatioAtOrAboveThreshold()
    {
        // Default threshold is 0.85 → need >= 0.85 * 4096 = 3481.6 → 3482 tokens
        var mgr = new ContextWindowBudgetManager(contextSize: 4096, evictionThreshold: 0.85);
        mgr.RecordExchange(promptTokens: 3482, completionTokens: 0);

        Assert.True(mgr.ShouldEvict);
        Assert.True(mgr.FillRatio >= 0.85);
    }

    [Fact]
    public void CalculateEvictionCount_ReturnsSensibleValue()
    {
        var mgr = new ContextWindowBudgetManager(contextSize: 4096);
        mgr.RecordExchange(promptTokens: 3000, completionTokens: 500); // 3500 used

        // At targetFillRatio=0.50: target = 2048 tokens → evict 3500-2048 = 1452
        var evict = mgr.CalculateEvictionCount(targetFillRatio: 0.50);
        Assert.Equal(1452, evict);
        Assert.True(evict > 0);
    }

    [Fact]
    public void Reset_ClearsUsedTokens()
    {
        var mgr = new ContextWindowBudgetManager(contextSize: 4096);
        mgr.RecordExchange(promptTokens: 1000, completionTokens: 500);
        Assert.NotEqual(0, mgr.UsedTokens);

        mgr.Reset();
        Assert.Equal(0, mgr.UsedTokens);
        Assert.False(mgr.ShouldEvict);
    }
}

// ── ModelDownloadServiceTests ─────────────────────────────────────────────────

public sealed class ModelDownloadServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ModelDownloadService _svc;

    public ModelDownloadServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BhenguAI_Test_{Guid.NewGuid():N}");
        _svc = new ModelDownloadService(_tempDir);
    }

    [Fact]
    public async Task IsModelCachedAsync_ReturnsFalse_ForUnknownModel()
    {
        var cached = await _svc.IsModelCachedAsync("nonexistent-model", CancellationToken.None);
        Assert.False(cached);
    }

    [Fact]
    public async Task DeleteModelAsync_IsNoOp_ForMissingModel()
    {
        // Should not throw
        var ex = await Record.ExceptionAsync(() =>
            _svc.DeleteModelAsync("ghost-model", CancellationToken.None));
        Assert.Null(ex);
    }

    [Fact]
    public async Task GetAvailableDiskSpaceBytesAsync_ReturnsPositiveValue()
    {
        var bytes = await _svc.GetAvailableDiskSpaceBytesAsync(CancellationToken.None);
        Assert.True(bytes > 0, $"Expected positive disk space but got {bytes}");
    }

    public void Dispose()
    {
        _svc.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
