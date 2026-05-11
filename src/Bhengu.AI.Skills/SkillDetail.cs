namespace Bhengu.AI.Skills;

/// <summary>
/// Full skill record — the complete definition of a single B! skill,
/// including the detailed instructions injected into the system prompt
/// when the skill is selected.
/// </summary>
/// <param name="Id">
/// Unique slug identifier, e.g. <c>"calendar-summariser"</c>.
/// Generated from <see cref="SkillDraft.Name"/> when not supplied explicitly.
/// </param>
/// <param name="Name">Human-readable display name.</param>
/// <param name="Description">
/// One-line summary of what this skill does. Used in system-prompt snippets
/// and skill-selection context blocks.
/// </param>
/// <param name="Instructions">
/// Detailed instructions for B! on how to execute this skill. May include
/// tool usage guidance, output format requirements, and examples.
/// </param>
/// <param name="Tags">
/// Free-form tags for filtering and search, e.g. <c>["productivity", "calendar"]</c>.
/// </param>
/// <param name="Source">Where this record was loaded from.</param>
/// <param name="LastModified">UTC timestamp of the most recent modification.</param>
public sealed record SkillDetail(
    string Id,
    string Name,
    string Description,
    string Instructions,
    IReadOnlyList<string> Tags,
    SkillSource Source,
    DateTimeOffset LastModified);
