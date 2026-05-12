namespace Bhengu.AI.Skills;

/// <summary>
/// Persistent store for B! skills. Skills are named, tagged capability
/// definitions that can be injected into the system prompt to guide B!'s
/// behaviour for specific tasks.
/// </summary>
public interface ISkillStore
{
    /// <summary>Returns all skills as lightweight summaries.</summary>
    Task<IReadOnlyList<SkillSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full detail for a single skill by ID.
    /// Returns <c>null</c> if no skill with the given ID exists.
    /// </summary>
    Task<SkillDetail?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns skills whose <c>Name</c>, <c>Description</c>, or <c>Tags</c>
    /// contain <paramref name="query"/> (case-insensitive substring match).
    /// Returns an empty list when <paramref name="query"/> is null or empty.
    /// </summary>
    Task<IReadOnlyList<SkillSummary>> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or replaces a skill. When <paramref name="id"/> is <c>null</c>
    /// or empty, a slug ID is auto-generated from <see cref="SkillDraft.Name"/>.
    /// </summary>
    Task<SkillDetail> UpsertAsync(string? id, SkillDraft draft, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the skill with the given ID. No-op if the skill does not exist.
    /// </summary>
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
