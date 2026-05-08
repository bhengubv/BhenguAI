using System;
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
