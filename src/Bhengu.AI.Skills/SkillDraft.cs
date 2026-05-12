namespace Bhengu.AI.Skills;

/// <summary>
/// Input model for creating or updating a skill via
/// <see cref="ISkillStore.UpsertAsync"/>.
/// </summary>
/// <param name="Name">Human-readable display name. Used to auto-generate the slug ID when none is provided.</param>
/// <param name="Description">One-line summary of what this skill does.</param>
/// <param name="Instructions">Detailed instructions for B! on how to execute this skill.</param>
/// <param name="Tags">Free-form tags for filtering and search.</param>
public sealed record SkillDraft(
    string Name,
    string Description,
    string Instructions,
    IReadOnlyList<string> Tags);
