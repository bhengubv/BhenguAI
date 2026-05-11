// JsonPersonaStore.cs
//
// Persists PersonaState to a JSON file on the local filesystem using a
// write-then-rename pattern (atomic from the OS perspective on Windows/Linux).
// Safe for single-process use; does NOT support concurrent multi-process
// writes.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Bhengu.AI.Memory
{
    /// <summary>
    /// <see cref="IPersonaStore"/> that persists each <see cref="PersonaState"/>
    /// as a JSON file in <paramref name="directory"/>, one file per
    /// <c>UserId</c> (safe filename derived from the ID).
    /// </summary>
    public sealed class JsonPersonaStore : IPersonaStore
    {
        private static readonly JsonSerializerOptions s_opts = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly string _directory;

        /// <param name="directory">
        /// Directory where persona JSON files are stored. Created if it does
        /// not exist.
        /// </param>
        public JsonPersonaStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Directory is required.", nameof(directory));
            _directory = directory;
            Directory.CreateDirectory(_directory);
        }

        /// <inheritdoc />
        public async Task<PersonaState> LoadAsync(string userId, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);

            var path = PersonaPath(userId);
            if (!File.Exists(path))
                return new PersonaState { UserId = userId };

            try
            {
                await using var fs = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 4096, useAsync: true);

                var persona = await JsonSerializer
                    .DeserializeAsync<PersonaState>(fs, s_opts, ct)
                    .ConfigureAwait(false);

                return persona ?? new PersonaState { UserId = userId };
            }
            catch
            {
                // Corrupted file — return fresh default, let the next Save overwrite.
                return new PersonaState { UserId = userId };
            }
        }

        /// <inheritdoc />
        public async Task SaveAsync(PersonaState persona, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(persona);

            persona.LastUpdatedUtc = DateTimeOffset.UtcNow;

            var target = PersonaPath(persona.UserId);
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
                    await JsonSerializer.SerializeAsync(fs, persona, s_opts, ct)
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

        private string PersonaPath(string userId)
        {
            // Sanitise userId to a safe filename component.
            var safe = string.Join("_",
                userId.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safe)) safe = "default";
            return Path.Combine(_directory, safe + ".persona.json");
        }
    }
}
