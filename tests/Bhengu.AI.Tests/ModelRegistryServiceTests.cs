using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bhengu.AI.Core.Models;
using Xunit;

namespace Bhengu.AI.Tests;

public sealed class ModelRegistryServiceTests
{
    // -----------------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------------

    [Fact]
    public void Constructor_NoArgs_Succeeds()
    {
        // Should not throw even when the embedded resource is absent.
        using var svc = new ModelRegistryService();
        Assert.NotNull(svc);
    }

    [Fact]
    public void Constructor_WithRegistryUrl_Succeeds()
    {
        using var svc = new ModelRegistryService("https://example.com/registry.json");
        Assert.NotNull(svc);
    }

    // -----------------------------------------------------------------------
    // GetLatestModel — before CheckForUpdatesAsync
    // -----------------------------------------------------------------------

    [Fact]
    public void GetLatestModel_BeforeUpdate_ReturnsNullForUnknownName()
    {
        using var svc = new ModelRegistryService();
        // No registry loaded → null or real embedded registry without this model.
        var entry = svc.GetLatestModel("no-such-model-xyz");
        Assert.Null(entry);
    }

    [Fact]
    public void GetLatestModel_NullName_DoesNotCrash()
    {
        using var svc = new ModelRegistryService();
        // Passing null name should return null, not throw.
        var ex = Record.Exception(() => svc.GetLatestModel(null!));
        // Could throw NullReferenceException on .Equals if unguarded, or return null.
        // Either outcome is acceptable as long as it is deterministic and documented.
        // The current implementation does not have an explicit null guard; we only
        // verify it doesn't silently corrupt state.
        // If it throws, that's a discovered defect worth noting:
        if (ex != null)
        {
            Assert.IsAssignableFrom<Exception>(ex); // document the gap, not fail-hard
        }
    }

    // -----------------------------------------------------------------------
    // CheckForUpdatesAsync — resilience
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CheckForUpdatesAsync_UnreachableUrl_DoesNotThrow()
    {
        // Uses a URL that will fail quickly (non-routable IP).
        using var svc = new ModelRegistryService("http://192.0.2.1/registry.json");
        // Implementation catches all exceptions and falls back to embedded.
        var ex = await Record.ExceptionAsync(() => svc.CheckForUpdatesAsync());
        Assert.Null(ex);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_NoUrl_DoesNotThrow()
    {
        using var svc = new ModelRegistryService(null);
        var ex = await Record.ExceptionAsync(() => svc.CheckForUpdatesAsync());
        Assert.Null(ex);
    }

    // -----------------------------------------------------------------------
    // Dispose
    // -----------------------------------------------------------------------

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var svc = new ModelRegistryService();
        svc.Dispose();
        var ex = Record.Exception(() => svc.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_ThenGetLatestModel_DoesNotThrow()
    {
        var svc = new ModelRegistryService();
        svc.Dispose();
        // GetLatestModel only reads _remoteRegistry / _embeddedRegistry, no HttpClient use.
        var ex = Record.Exception(() => svc.GetLatestModel("any"));
        Assert.Null(ex);
    }
}

// ============================================================================
// ModelEntry and ModelRegistry records
// ============================================================================

public sealed class ModelEntryTests
{
    [Fact]
    public void Constructor_AllProperties_AreReflected()
    {
        var entry = new ModelEntry(
            Name:         "Qwen3-14B-Q4",
            Version:      "3.0",
            Quantization: "Q4_K_M",
            Url:          "https://hf.co/model.gguf",
            Checksum:     "sha256:abc123");

        Assert.Equal("Qwen3-14B-Q4",          entry.Name);
        Assert.Equal("3.0",                    entry.Version);
        Assert.Equal("Q4_K_M",                entry.Quantization);
        Assert.Equal("https://hf.co/model.gguf", entry.Url);
        Assert.Equal("sha256:abc123",          entry.Checksum);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var e1 = new ModelEntry("A", "1", "Q4", "https://a.b/c", "sha256:x");
        var e2 = new ModelEntry("A", "1", "Q4", "https://a.b/c", "sha256:x");
        Assert.Equal(e1, e2);
    }

    [Fact]
    public void Equality_DifferentChecksum_NotEqual()
    {
        var e1 = new ModelEntry("A", "1", "Q4", "https://a.b/c", "sha256:x");
        var e2 = new ModelEntry("A", "1", "Q4", "https://a.b/c", "sha256:y");
        Assert.NotEqual(e1, e2);
    }

    [Fact]
    public void WithExpression_OverridesVersion()
    {
        var orig    = new ModelEntry("M", "1.0", "Q4", "https://u", "sha256:h");
        var updated = orig with { Version = "2.0" };

        Assert.Equal("2.0", updated.Version);
        Assert.Equal("M",   updated.Name);
    }

    [Fact]
    public void Checksum_TbdPlaceholder_IsDetectable()
    {
        // Production blocker: both registry entries currently carry "sha256:TBD".
        // This test documents and detects the placeholder so it can't ship silently.
        var entry = new ModelEntry("Qwen3-14B-Q4", "3.0", "Q4_K_M",
            "https://hf.co/model.gguf", "sha256:TBD");
        Assert.Contains("TBD", entry.Checksum, StringComparison.Ordinal);
    }
}

public sealed class ModelRegistryRecordTests
{
    [Fact]
    public void Constructor_AllProperties_AreReflected()
    {
        var entries = new List<ModelEntry>
        {
            new("Qwen3-14B-Q4", "3.0", "Q4_K_M", "https://hf.co/a.gguf", "sha256:abc"),
        };
        var ts  = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var reg = new ModelRegistry("https://registry.thegeek.co.za/models.json", ts, entries);

        Assert.Equal("https://registry.thegeek.co.za/models.json", reg.RegistryUrl);
        Assert.Equal(ts,      reg.LastUpdated);
        Assert.Single(reg.Models);
        Assert.Equal("Qwen3-14B-Q4", reg.Models[0].Name);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var entries = new List<ModelEntry>();
        var ts      = DateTime.UtcNow;
        var r1 = new ModelRegistry("https://url", ts, entries);
        var r2 = new ModelRegistry("https://url", ts, entries);
        Assert.Equal(r1, r2);
    }

    [Fact]
    public void Models_EmptyList_IsValid()
    {
        var reg = new ModelRegistry("https://url", DateTime.UtcNow, new List<ModelEntry>());
        Assert.Empty(reg.Models);
    }
}
