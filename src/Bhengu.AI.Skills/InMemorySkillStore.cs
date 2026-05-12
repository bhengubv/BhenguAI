using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Bhengu.AI.Skills;

/// <summary>
/// Thread-safe in-memory <see cref="ISkillStore"/> backed by a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>. Useful for tests and
/// hosts that assemble skills programmatically at startup.
/// </summary>
public sealed class InMemorySkillStore : ISkillStore
{
    private readonly ConcurrentDictionary<string, SkillDetail> _skills = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<IReadOnlyList<SkillSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var results = _skills.Values
            .Select(ToSummary)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<SkillSummary>>(results);
    }

    /// <inheritdoc />
    public Task<SkillDetail?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _skills.TryGetValue(id, out var detail);
        return Task.FromResult<SkillDetail?>(detail);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SkillSummary>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult<IReadOnlyList<SkillSummary>>(Array.Empty<SkillSummary>());

        var q = query.Trim();
        var results = _skills.Values
            .Where(s => MatchesQuery(s, q))
            .Select(ToSummary)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult<IReadOnlyList<SkillSummary>>(results);
    }

    /// <inheritdoc />
    public Task<SkillDetail> UpsertAsync(string? id, SkillDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var effectiveId = string.IsNullOrWhiteSpace(id) ? GenerateSlug(draft.Name) : id.Trim();

        var detail = new SkillDetail(
            Id: effectiveId,
            Name: draft.Name,
            Description: draft.Description,
            Instructions: draft.Instructions,
            Tags: draft.Tags ?? Array.Empty<string>(),
            Source: SkillSource.InMemory,
            LastModified: DateTimeOffset.UtcNow);

        _skills[effectiveId] = detail;
        return Task.FromResult(detail);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _skills.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static SkillSummary ToSummary(SkillDetail d) =>
        new(d.Id, d.Name, d.Description, d.Tags, d.Source);

    private static bool MatchesQuery(SkillDetail s, string query) =>
        s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        s.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        s.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Converts a display name to a URL-safe lowercase slug.
    /// "My Skill" → "my-skill"
    /// </summary>
    public static string GenerateSlug(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Guid.NewGuid().ToString("N");
        var slug = name.Trim().ToLowerInvariant();
        slug = Regex.Replace(slug, @"\s+", "-");
        slug = Regex.Replace(slug, @"[^a-z0-9\-]", string.Empty);
        slug = Regex.Replace(slug, @"-{2,}", "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? Guid.NewGuid().ToString("N") : slug;
    }
}
