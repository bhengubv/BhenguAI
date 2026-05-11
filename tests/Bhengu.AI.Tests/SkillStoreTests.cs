// SkillStoreTests.cs — Tests for Bhengu.AI.Skills:
//   InMemorySkillStore, FileSkillStore, SkillContextBuilder, slug generation.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bhengu.AI.Skills;
using Xunit;

namespace Bhengu.AI.Tests;

// ============================================================================
// InMemorySkillStore
// ============================================================================

public sealed class InMemorySkillStoreTests
{
    private static SkillDraft MakeDraft(string name, string desc = "A skill", string[]? tags = null) =>
        new(name, desc, $"Instructions for {name}", tags ?? Array.Empty<string>());

    [Fact]
    public async Task UpsertAndList_RoundTrip()
    {
        var store = new InMemorySkillStore();
        await store.UpsertAsync(null, MakeDraft("My Skill"));
        var list = await store.ListAsync();
        Assert.Single(list);
        Assert.Equal("my-skill", list[0].Id);
    }

    [Fact]
    public async Task UpsertWithExplicitId_UsesProvidedId()
    {
        var store = new InMemorySkillStore();
        await store.UpsertAsync("custom-id", MakeDraft("Something"));
        var detail = await store.GetAsync("custom-id");
        Assert.NotNull(detail);
        Assert.Equal("custom-id", detail.Id);
    }

    [Fact]
    public async Task Get_NonExistentId_ReturnsNull()
    {
        var store = new InMemorySkillStore();
        var result = await store.GetAsync("does-not-exist");
        Assert.Null(result);
    }

    [Fact]
    public async Task Delete_RemovesSkill()
    {
        var store = new InMemorySkillStore();
        await store.UpsertAsync(null, MakeDraft("Delete Me"));
        await store.DeleteAsync("delete-me");
        var list = await store.ListAsync();
        Assert.Empty(list);
    }

    [Fact]
    public async Task Delete_NonExistentId_IsNoOp()
    {
        var store = new InMemorySkillStore();
        var ex = await Record.ExceptionAsync(() => store.DeleteAsync("ghost"));
        Assert.Null(ex);
    }

    [Fact]
    public async Task Search_MatchesName_CaseInsensitive()
    {
        var store = new InMemorySkillStore();
        await store.UpsertAsync(null, MakeDraft("Calendar Summariser"));
        var results = await store.SearchAsync("calendar");
        Assert.Single(results);
    }

    [Fact]
    public async Task Search_MatchesDescription()
    {
        var store = new InMemorySkillStore();
        await store.UpsertAsync(null, MakeDraft("Weather", "Gives you the forecast"));
        var results = await store.SearchAsync("forecast");
        Assert.Single(results);
    }

    [Fact]
    public async Task Search_MatchesTags()
    {
        var store = new InMemorySkillStore();
        await store.UpsertAsync(null, MakeDraft("Email", tags: new[] { "productivity", "gmail" }));
        var results = await store.SearchAsync("gmail");
        Assert.Single(results);
    }

    [Fact]
    public async Task Search_NoMatch_ReturnsEmpty()
    {
        var store = new InMemorySkillStore();
        await store.UpsertAsync(null, MakeDraft("Calendar"));
        var results = await store.SearchAsync("zzz-no-match");
        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_EmptyQuery_ReturnsEmpty()
    {
        var store = new InMemorySkillStore();
        await store.UpsertAsync(null, MakeDraft("Something"));
        var results = await store.SearchAsync(string.Empty);
        Assert.Empty(results);
    }

    [Fact]
    public async Task Upsert_UpdatesExistingSkill()
    {
        var store = new InMemorySkillStore();
        await store.UpsertAsync("abc", MakeDraft("Old Name"));
        await store.UpsertAsync("abc", MakeDraft("New Name"));
        var detail = await store.GetAsync("abc");
        Assert.Equal("New Name", detail!.Name);
    }

    [Fact]
    public void GenerateSlug_ConvertsSpacesToHyphens()
    {
        Assert.Equal("my-skill", InMemorySkillStore.GenerateSlug("My Skill"));
    }

    [Fact]
    public void GenerateSlug_RemovesSpecialChars()
    {
        Assert.Equal("hello-world", InMemorySkillStore.GenerateSlug("Hello, World!"));
    }

    [Fact]
    public void GenerateSlug_CollapsesMultipleHyphens()
    {
        Assert.Equal("a-b", InMemorySkillStore.GenerateSlug("a  -  b"));
    }
}

// ============================================================================
// FileSkillStore — SKILL.md parsing
// ============================================================================

public sealed class FileSkillStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static string BuildSkillMd(string id, string name, string desc,
        string tags = "[test]", string instructions = "Do the thing.")
        => $"---\nid: {id}\nname: {name}\ndescription: {desc}\ntags: {tags}\n---\n\n{instructions}";

    [Fact]
    public async Task UpsertAndList_RoundTrip()
    {
        var store = new FileSkillStore(_tempDir);
        var draft = new SkillDraft("File Skill", "Loaded from file", "Do stuff", new[] { "test" });
        await store.UpsertAsync(null, draft);
        var list = await store.ListAsync();
        Assert.Single(list);
        Assert.Equal("file-skill", list[0].Id);
    }

    [Fact]
    public async Task UpsertAndGet_ReadsBackCorrectly()
    {
        var store = new FileSkillStore(_tempDir);
        var draft = new SkillDraft("My Skill", "Desc", "Follow these steps.", new[] { "tag1" });
        await store.UpsertAsync("my-skill", draft);
        var detail = await store.GetAsync("my-skill");
        Assert.NotNull(detail);
        Assert.Equal("My Skill", detail.Name);
        Assert.Equal("Desc", detail.Description);
        Assert.Contains("Follow these steps", detail.Instructions);
        Assert.Contains("tag1", detail.Tags);
    }

    [Fact]
    public async Task Delete_RemovesFile()
    {
        var store = new FileSkillStore(_tempDir);
        await store.UpsertAsync("to-delete", new SkillDraft("X", "X", "X", Array.Empty<string>()));
        await store.DeleteAsync("to-delete");
        var detail = await store.GetAsync("to-delete");
        Assert.Null(detail);
    }

    [Fact]
    public void ParseSkillFile_ParsesFrontmatterCorrectly()
    {
        var content = BuildSkillMd("cal", "Calendar", "Calendar skill", "[calendar, productivity]", "Use the calendar tool.");
        var detail = FileSkillStore.ParseSkillFile(content, "cal", "/tmp/cal.md");
        Assert.NotNull(detail);
        Assert.Equal("cal", detail.Id);
        Assert.Equal("Calendar", detail.Name);
        Assert.Equal("Calendar skill", detail.Description);
        Assert.Contains("calendar", detail.Tags);
        Assert.Contains("productivity", detail.Tags);
        Assert.Equal("Use the calendar tool.", detail.Instructions);
    }

    [Fact]
    public void ParseSkillFile_MissingId_FallsBackToFileName()
    {
        var content = "---\nname: Test\ndescription: Desc\ntags: []\n---\nInstructions.";
        var detail = FileSkillStore.ParseSkillFile(content, "fallback-id", "/tmp/fallback-id.md");
        Assert.NotNull(detail);
        Assert.Equal("fallback-id", detail.Id);
    }

    [Fact]
    public void ParseSkillFile_NullContent_ReturnsNull()
    {
        var detail = FileSkillStore.ParseSkillFile(string.Empty, "x", "/tmp/x.md");
        Assert.Null(detail);
    }

    [Fact]
    public void ParseSkillFile_NoFrontmatterDelimiter_ReturnsNull()
    {
        var content = "Just plain text, no frontmatter.";
        var detail = FileSkillStore.ParseSkillFile(content, "x", "/tmp/x.md");
        Assert.Null(detail);
    }

    [Fact]
    public async Task Search_FindsByDescription()
    {
        var store = new FileSkillStore(_tempDir);
        await store.UpsertAsync("search-test", new SkillDraft("X", "Unique description here", "X", Array.Empty<string>()));
        var results = await store.SearchAsync("Unique description");
        Assert.Single(results);
    }
}

// ============================================================================
// SkillContextBuilder
// ============================================================================

public sealed class SkillContextBuilderTests
{
    [Fact]
    public async Task BuildContextAsync_EmptyStore_ReturnsEmptyString()
    {
        var store = new InMemorySkillStore();
        var builder = new SkillContextBuilder(store);
        var result = await builder.BuildContextAsync("help me with calendar");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task BuildContextAsync_EmptyQuery_ReturnsEmptyString()
    {
        var store = new InMemorySkillStore();
        await store.UpsertAsync(null, new SkillDraft("Calendar", "Cal skill", "Use cal.", Array.Empty<string>()));
        var builder = new SkillContextBuilder(store);
        var result = await builder.BuildContextAsync(string.Empty);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task BuildContextAsync_MatchingSkill_ContainsSkillName()
    {
        var store = new InMemorySkillStore();
        await store.UpsertAsync("calendar-summariser",
            new SkillDraft("Calendar Summariser", "Summarises events", "Call the calendar tool.", new[] { "calendar" }));
        var builder = new SkillContextBuilder(store);
        var result = await builder.BuildContextAsync("show me my schedule");
        Assert.Contains("calendar-summariser", result);
        Assert.Contains("Summarises events", result);
    }

    [Fact]
    public async Task BuildContextAsync_ContainsInstructions()
    {
        var store = new InMemorySkillStore();
        await store.UpsertAsync("test-skill",
            new SkillDraft("Test", "Desc", "Follow these exact steps.", Array.Empty<string>()));
        var builder = new SkillContextBuilder(store);
        var result = await builder.BuildContextAsync("test-skill");
        Assert.Contains("Follow these exact steps", result);
    }

    [Fact]
    public async Task BuildContextAsync_RespectsMaxSkillsLimit()
    {
        var store = new InMemorySkillStore();
        for (int i = 0; i < 10; i++)
            await store.UpsertAsync($"skill-{i}",
                new SkillDraft($"Skill {i}", "common desc", "Do it.", new[] { "common" }));

        var builder = new SkillContextBuilder(store, maxSkills: 3);
        var result = await builder.BuildContextAsync("common");

        // Count how many skill IDs appear in the result
        var count = Enumerable.Range(0, 10).Count(i => result.Contains($"skill-{i}"));
        Assert.True(count <= 3);
    }

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SkillContextBuilder(null!));
    }

    [Fact]
    public void Constructor_ZeroMaxSkills_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SkillContextBuilder(new InMemorySkillStore(), maxSkills: 0));
    }
}
