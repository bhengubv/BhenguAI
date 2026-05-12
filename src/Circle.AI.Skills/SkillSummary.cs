namespace Circle.AI.Skills;

/// <summary>
/// Lightweight projection of a <see cref="SkillDetail"/> used in list and
/// search results. Does not include the full <c>Instructions</c> text.
/// </summary>
/// <param name="Id">Unique slug identifier.</param>
/// <param name="Name">Human-readable display name.</param>
/// <param name="Description">One-line summary of what this skill does.</param>
/// <param name="Tags">Free-form tags for filtering.</param>
/// <param name="Source">Where this record was loaded from.</param>
public sealed record SkillSummary(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> Tags,
    SkillSource Source);
