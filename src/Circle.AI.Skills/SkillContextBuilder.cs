using System.Text;

namespace Circle.AI.Skills;

/// <summary>
/// Selects the most relevant skills for a user query and formats them as a
/// system-prompt context block. Drop this into the B! system prompt enrichment
/// pipeline to give the model knowledge of available skills before each call.
/// </summary>
public sealed class SkillContextBuilder
{
    private readonly ISkillStore _store;
    private readonly int _maxSkills;

    /// <summary>
    /// Initialises the builder.
    /// </summary>
    /// <param name="store">Source of available skills.</param>
    /// <param name="maxSkills">
    /// Maximum number of skills to include in the context block. Default 5.
    /// </param>
    public SkillContextBuilder(ISkillStore store, int maxSkills = 5)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (maxSkills < 1) throw new ArgumentOutOfRangeException(nameof(maxSkills), "Must be at least 1.");
        _store = store;
        _maxSkills = maxSkills;
    }

    /// <summary>
    /// Returns a formatted system-prompt block listing the most relevant
    /// skills for <paramref name="userQuery"/>. Returns an empty string when
    /// the store is empty or no skills match.
    /// </summary>
    /// <param name="userQuery">The user's current message or intent.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string> BuildContextAsync(
        string userQuery,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userQuery))
            return string.Empty;

        // Search for matching skills; fall back to the full list if nothing matches.
        var matches = await _store.SearchAsync(userQuery, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<SkillSummary> candidates;
        if (matches.Count > 0)
        {
            candidates = matches.Take(_maxSkills).ToList();
        }
        else
        {
            var all = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
            if (all.Count == 0) return string.Empty;
            candidates = all.Take(_maxSkills).ToList();
        }

        // Load full detail so we can include instructions.
        var sb = new StringBuilder();
        sb.AppendLine("## Available Skills");

        foreach (var summary in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var detail = await _store.GetAsync(summary.Id, cancellationToken).ConfigureAwait(false);
            if (detail is null) continue;

            sb.AppendLine();
            sb.AppendLine($"**{detail.Id}** — {detail.Description}");
            if (!string.IsNullOrWhiteSpace(detail.Instructions))
            {
                // Indent instructions for readability inside the system prompt.
                foreach (var line in detail.Instructions.Split('\n'))
                    sb.AppendLine($"  {line}");
            }
        }

        return sb.ToString().TrimEnd();
    }
}
