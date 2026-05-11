using System.Text;

namespace Bhengu.AI.Skills;

/// <summary>
/// <see cref="ISkillStore"/> backed by SKILL.md files in a directory.
/// Each file uses YAML front-matter for metadata and Markdown body for
/// the skill instructions — the same format used by Hermes OS1.
/// </summary>
/// <remarks>
/// <para>Expected file format:</para>
/// <code>
/// ---
/// id: calendar-summariser
/// name: Calendar Summariser
/// description: Summarises upcoming calendar events into a concise digest
/// tags: [productivity, calendar, summaries]
/// ---
///
/// ## Instructions
/// When the user asks about their schedule, call the calendar tool…
/// </code>
/// <para>
/// The <c>id</c> front-matter field is optional; when absent the file name
/// (without extension) is used as the skill ID.
/// </para>
/// </remarks>
public sealed class FileSkillStore : ISkillStore
{
    private readonly string _directoryPath;

    /// <summary>
    /// Initialises the store. Creates <paramref name="directoryPath"/> if it
    /// does not yet exist.
    /// </summary>
    public FileSkillStore(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        _directoryPath = directoryPath;
        Directory.CreateDirectory(_directoryPath);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SkillSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<SkillSummary>();
        foreach (var file in GetSkillFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var detail = await ReadSkillFileAsync(file, cancellationToken).ConfigureAwait(false);
            if (detail is not null)
                results.Add(ToSummary(detail));
        }
        return results.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <inheritdoc />
    public async Task<SkillDetail?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        foreach (var file in GetSkillFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var detail = await ReadSkillFileAsync(file, cancellationToken).ConfigureAwait(false);
            if (detail is not null && string.Equals(detail.Id, id, StringComparison.OrdinalIgnoreCase))
                return detail;
        }
        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SkillSummary>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SkillSummary>();

        var q = query.Trim();
        var results = new List<SkillSummary>();
        foreach (var file in GetSkillFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var detail = await ReadSkillFileAsync(file, cancellationToken).ConfigureAwait(false);
            if (detail is not null && MatchesQuery(detail, q))
                results.Add(ToSummary(detail));
        }
        return results.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <inheritdoc />
    public async Task<SkillDetail> UpsertAsync(string? id, SkillDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var effectiveId = string.IsNullOrWhiteSpace(id)
            ? InMemorySkillStore.GenerateSlug(draft.Name)
            : id.Trim();

        var filePath = Path.Combine(_directoryPath, $"{effectiveId}.md");
        var tags = draft.Tags is { Count: > 0 }
            ? $"[{string.Join(", ", draft.Tags)}]"
            : "[]";

        var content = new StringBuilder();
        content.AppendLine("---");
        content.AppendLine($"id: {effectiveId}");
        content.AppendLine($"name: {draft.Name}");
        content.AppendLine($"description: {draft.Description}");
        content.AppendLine($"tags: {tags}");
        content.AppendLine("---");
        content.AppendLine();
        content.Append(draft.Instructions);

        await File.WriteAllTextAsync(filePath, content.ToString(), Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);

        return new SkillDetail(
            Id: effectiveId,
            Name: draft.Name,
            Description: draft.Description,
            Instructions: draft.Instructions,
            Tags: draft.Tags ?? Array.Empty<string>(),
            Source: SkillSource.File,
            LastModified: DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var filePath = Path.Combine(_directoryPath, $"{id}.md");
        if (File.Exists(filePath))
            File.Delete(filePath);
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // Parsing
    // ------------------------------------------------------------------

    private IEnumerable<string> GetSkillFiles() =>
        Directory.EnumerateFiles(_directoryPath, "*.md", SearchOption.TopDirectoryOnly);

    private static async Task<SkillDetail?> ReadSkillFileAsync(
        string filePath, CancellationToken ct)
    {
        string content;
        try
        {
            content = await File.ReadAllTextAsync(filePath, Encoding.UTF8, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        return ParseSkillFile(content, Path.GetFileNameWithoutExtension(filePath), filePath);
    }

    public static SkillDetail? ParseSkillFile(string content, string fileNameWithoutExt, string filePath)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        // Locate the YAML front-matter block between the first two "---" lines.
        var lines = content.Replace("\r\n", "\n").Split('\n');
        if (lines.Length < 2 || lines[0].Trim() != "---")
            return null;

        var frontMatterEnd = -1;
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---") { frontMatterEnd = i; break; }
        }
        if (frontMatterEnd < 0) return null;

        // Parse front-matter key: value pairs.
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < frontMatterEnd; i++)
        {
            var line = lines[i];
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            meta[key] = value;
        }

        var id = meta.TryGetValue("id", out var idVal) && !string.IsNullOrWhiteSpace(idVal)
            ? idVal
            : fileNameWithoutExt;
        var name = meta.TryGetValue("name", out var nameVal) ? nameVal : id;
        var description = meta.TryGetValue("description", out var descVal) ? descVal : string.Empty;
        var tags = ParseTagsList(meta.TryGetValue("tags", out var tagsVal) ? tagsVal : string.Empty);

        // Everything after the closing "---" is the instructions body.
        var instructionsLines = lines.Skip(frontMatterEnd + 1);
        var instructions = string.Join("\n", instructionsLines).Trim();

        var lastModified = File.Exists(filePath)
            ? new DateTimeOffset(File.GetLastWriteTimeUtc(filePath), TimeSpan.Zero)
            : DateTimeOffset.UtcNow;

        return new SkillDetail(id, name, description, instructions, tags, SkillSource.File, lastModified);
    }

    /// <summary>
    /// Parses a YAML inline list like <c>[a, b, c]</c> or a bare scalar.
    /// </summary>
    private static IReadOnlyList<string> ParseTagsList(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        raw = raw.Trim();
        if (raw.StartsWith('[') && raw.EndsWith(']'))
            raw = raw[1..^1];
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Where(t => !string.IsNullOrWhiteSpace(t))
                  .ToArray();
    }

    private static SkillSummary ToSummary(SkillDetail d) =>
        new(d.Id, d.Name, d.Description, d.Tags, d.Source);

    private static bool MatchesQuery(SkillDetail s, string query) =>
        s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        s.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        s.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase));
}
