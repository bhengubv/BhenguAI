using System;
using System.IO;
using System.Threading.Tasks;
using Bhengu.AI.Memory;
using Xunit;

namespace Bhengu.AI.Tests;

// ============================================================================
// PersonaState
// ============================================================================

public sealed class PersonaStateTests
{
    [Fact]
    public void SatisfactionScore_FewSignals_ReturnsNull()
    {
        var p = new PersonaState
        {
            PositiveSignals = 5,
            NegativeSignals = 3,
        };
        // Fewer than 10 total — score undefined.
        Assert.Null(p.SatisfactionScore);
    }

    [Fact]
    public void SatisfactionScore_EnoughSignals_ReturnsRatio()
    {
        var p = new PersonaState
        {
            PositiveSignals = 8,
            NegativeSignals = 2,
        };
        Assert.NotNull(p.SatisfactionScore);
        Assert.Equal(0.8, p.SatisfactionScore!.Value, precision: 5);
    }

    [Fact]
    public void SatisfactionScore_AllPositive_IsOne()
    {
        var p = new PersonaState
        {
            PositiveSignals = 10,
            NegativeSignals = 0,
        };
        Assert.Equal(1.0, p.SatisfactionScore!.Value, precision: 5);
    }

    [Fact]
    public void ToSystemPromptHint_DefaultState_ReturnsEmptyString()
    {
        var p = new PersonaState(); // verbosity=balanced, formality=neutral
        Assert.Equal(string.Empty, p.ToSystemPromptHint());
    }

    [Fact]
    public void ToSystemPromptHint_BriefVerbosity_IncludesHint()
    {
        var p = new PersonaState { Verbosity = "brief" };
        var hint = p.ToSystemPromptHint();
        Assert.Contains("brief", hint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[User preferences]", hint);
    }

    [Fact]
    public void ToSystemPromptHint_CasualFormality_IncludesTone()
    {
        var p = new PersonaState { Formality = "casual" };
        var hint = p.ToSystemPromptHint();
        Assert.Contains("casual", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToSystemPromptHint_FormalFormality_IncludesProfessional()
    {
        var p = new PersonaState { Formality = "formal" };
        var hint = p.ToSystemPromptHint();
        Assert.Contains("formal", hint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToSystemPromptHint_PreferredLocale_IncludesLocale()
    {
        var p = new PersonaState { PreferredLocale = "zu-ZA" };
        var hint = p.ToSystemPromptHint();
        Assert.Contains("zu-ZA", hint);
    }
}

// ============================================================================
// InMemoryPersonaStore
// ============================================================================

public sealed class InMemoryPersonaStoreTests
{
    [Fact]
    public async Task LoadAsync_UnknownUser_ReturnsFreshPersona()
    {
        var store = new InMemoryPersonaStore();
        var p = await store.LoadAsync("alice");

        Assert.NotNull(p);
        Assert.Equal("alice", p.UserId);
        Assert.Equal("balanced", p.Verbosity);   // default
    }

    [Fact]
    public async Task SaveAsync_PersistsState_LoadReturnsUpdated()
    {
        var store = new InMemoryPersonaStore();

        var p = await store.LoadAsync("bob");
        p.Verbosity = "brief";
        await store.SaveAsync(p);

        var loaded = await store.LoadAsync("bob");
        Assert.Equal("brief", loaded.Verbosity);
    }

    [Fact]
    public async Task SaveAsync_UpdatesLastUpdatedUtc()
    {
        var store = new InMemoryPersonaStore();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var p = new PersonaState { UserId = "carol" };
        await store.SaveAsync(p);

        var loaded = await store.LoadAsync("carol");
        Assert.True(loaded.LastUpdatedUtc >= before);
    }

    [Fact]
    public async Task LoadAsync_NullOrEmptyUserId_Throws()
    {
        var store = new InMemoryPersonaStore();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.LoadAsync(""));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.LoadAsync(null!));
    }

    [Fact]
    public async Task SaveAsync_NullPersona_Throws()
    {
        var store = new InMemoryPersonaStore();
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.SaveAsync(null!));
    }
}

// ============================================================================
// JsonPersonaStore
// ============================================================================

public sealed class JsonPersonaStoreTests : IDisposable
{
    private readonly string _dir;

    public JsonPersonaStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bhenguai_persona_tests_" + Guid.NewGuid());
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task SaveAsync_WritesFileToDisk()
    {
        var store = new JsonPersonaStore(_dir);
        var p = new PersonaState { UserId = "diana", Verbosity = "detailed" };

        await store.SaveAsync(p);

        Assert.True(Directory.Exists(_dir));
        var files = Directory.GetFiles(_dir, "*.persona.json");
        Assert.NotEmpty(files);
    }

    [Fact]
    public async Task LoadAsync_AfterSave_ReturnsPersistedData()
    {
        var store = new JsonPersonaStore(_dir);

        var p = new PersonaState { UserId = "evan", Verbosity = "brief", Formality = "formal" };
        await store.SaveAsync(p);

        var loaded = await store.LoadAsync("evan");
        Assert.Equal("brief", loaded.Verbosity);
        Assert.Equal("formal", loaded.Formality);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsFreshPersona()
    {
        var store = new JsonPersonaStore(_dir);
        var p = await store.LoadAsync("nobody");

        Assert.Equal("nobody", p.UserId);
        Assert.Equal("balanced", p.Verbosity);
    }

    [Fact]
    public async Task LoadAsync_CorruptedFile_ReturnsFreshPersona()
    {
        // Write garbage JSON.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "corrupt.persona.json"), "{{not valid json}}}");

        var store = new JsonPersonaStore(_dir);
        // "corrupt" is the sanitised user ID for a user called "corrupt".
        var p = await store.LoadAsync("corrupt");

        Assert.Equal("corrupt", p.UserId);
    }

    [Fact]
    public void Constructor_EmptyDirectory_Throws()
    {
        Assert.Throws<ArgumentException>(() => new JsonPersonaStore(""));
    }

    [Fact]
    public void Constructor_CreatesDirectoryIfMissing()
    {
        var nonExistent = Path.Combine(_dir, "sub", "deep");
        var store = new JsonPersonaStore(nonExistent);

        Assert.True(Directory.Exists(nonExistent));
    }
}
