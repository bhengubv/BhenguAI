namespace Bhengu.AI.Skills;

/// <summary>Indicates where a <see cref="SkillDetail"/> originated.</summary>
public enum SkillSource
{
    /// <summary>Loaded from a SKILL.md file on disk.</summary>
    File,
    /// <summary>Created programmatically and held in memory.</summary>
    InMemory,
    /// <summary>Fetched from a remote skill registry.</summary>
    Remote
}
