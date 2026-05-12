// JsonAffectStore.cs
//
// Persists AffectState to a JSON file on the local filesystem using a
// write-then-rename pattern (atomic from the OS perspective on Windows/Linux).
// Safe for single-process use; does NOT support concurrent multi-process writes.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Circle.AI.Memory;

/// <summary>
/// <see cref="IAffectStore"/> that persists each <see cref="AffectState"/>
/// as a JSON file in <paramref name="directory"/>, one file per
/// <c>UserId</c> (safe filename derived from the ID).
/// </summary>
public sealed class JsonAffectStore : IAffectStore
{
    private static readonly JsonSerializerOptions s_opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _directory;

    /// <param name="directory">
    /// Directory where affect state JSON files are stored. Created if it does
    /// not exist.
    /// </param>
    public JsonAffectStore(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Directory is required.", nameof(directory));
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    /// <inheritdoc />
    public async Task<AffectState> LoadAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var path = AffectPath(userId);
        if (!File.Exists(path))
            return new AffectState { UserId = userId };

        try
        {
            await using var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, useAsync: true);

            var state = await JsonSerializer
                .DeserializeAsync<AffectState>(fs, s_opts, ct)
                .ConfigureAwait(false);

            return state ?? new AffectState { UserId = userId };
        }
        catch
        {
            // Corrupted file — return fresh default, let the next Save overwrite.
            return new AffectState { UserId = userId };
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(AffectState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.LastUpdatedUtc = DateTimeOffset.UtcNow;

        var target = AffectPath(state.UserId);
        // Unique temp file per save so concurrent saves for the same user
        // never contend on the same .tmp path (each rename is atomic;
        // last-writer-wins across concurrent saves is the intended contract).
        var tmp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await using (var fs = new FileStream(
                tmp, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(fs, state, s_opts, ct)
                    .ConfigureAwait(false);
            }

            // Atomic replace on Windows (MoveFileEx with REPLACE_EXISTING).
            File.Move(tmp, target, overwrite: true);
        }
        catch
        {
            // Best-effort. If write fails, don't crash inference.
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private string AffectPath(string userId)
    {
        // Sanitise userId to a safe filename component.
        var safe = string.Join("_",
            userId.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safe)) safe = "default";
        return Path.Combine(_directory, safe + ".affect.json");
    }
}
