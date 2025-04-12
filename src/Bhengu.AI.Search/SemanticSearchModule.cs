using Bhengu.AI.Core;
using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Bhengu.AI.Search
{
    public sealed class SemanticSearchModule : IBhenguModule
    {
        private SQLiteConnection? _db;
        private IEmbeddingService? _embeddingService;
        private bool _disposed;

        public string ModuleName => "SemanticSearch";
        public bool IsModelLoaded => _db != null && _embeddingService?.IsModelLoaded == true;

        public async Task InitAsync(BhenguEngine engine)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SemanticSearchModule));

            _embeddingService = engine.GetModule<IEmbeddingService>();
            _db = new SQLiteConnection("search.db");
            _db.CreateTable<SearchItem>();

            await Task.CompletedTask;
        }

        public void AddItem(string text, double lat, double lng)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SemanticSearchModule));
            if (_db == null || _embeddingService == null)
                throw new InvalidOperationException("Module not initialized");

            var embedding = _embeddingService.GenerateEmbedding(text);
            _db.Insert(new SearchItem
            {
                Text = text,
                Lat = lat,
                Lng = lng,
                EmbeddingBlob = ConvertToBlob(embedding)
            });
        }

        public List<SearchResult> Search(string query, double lat, double lng, double radiusKm)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SemanticSearchModule));
            if (_db == null || _embeddingService == null)
                throw new InvalidOperationException("Module not initialized");

            var queryEmbedding = _embeddingService.GenerateEmbedding(query);
            var results = new List<SearchResult>();

            foreach (var item in _db.Table<SearchItem>())
            {
                var distanceKm = HaversineDistance(lat, lng, item.Lat, item.Lng);
                if (distanceKm <= radiusKm)
                {
                    results.Add(new SearchResult(
                        item.Text,
                        distanceKm,
                        CosineSimilarity(queryEmbedding, ConvertFromBlob(item.EmbeddingBlob))
                    ));
                }
            }

            return results.OrderByDescending(r => r.Score).ToList();
        }

        private static byte[] ConvertToBlob(float[] embedding)
        {
            var blob = new byte[embedding.Length * sizeof(float)];
            Buffer.BlockCopy(embedding, 0, blob, 0, blob.Length);
            return blob;
        }

        private static float[] ConvertFromBlob(byte[] blob)
        {
            var embedding = new float[blob.Length / sizeof(float)];
            Buffer.BlockCopy(blob, 0, embedding, 0, blob.Length);
            return embedding;
        }

        private static float CosineSimilarity(float[] vec1, float[] vec2)
        {
            float dot = 0, mag1 = 0, mag2 = 0;
            for (int i = 0; i < vec1.Length; i++)
            {
                dot += vec1[i] * vec2[i];
                mag1 += vec1[i] * vec1[i];
                mag2 += vec2[i] * vec2[i];
            }
            return dot / (MathF.Sqrt(mag1) * MathF.Sqrt(mag2));
        }

        private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0;
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private static double ToRadians(double angle) => angle * (Math.PI / 180);

        public void Dispose()
        {
            if (_disposed) return;
            _db?.Dispose();
            _disposed = true;
        }

        [Table("SearchItems")]
        public class SearchItem
        {
            [PrimaryKey, AutoIncrement]
            public long Id { get; set; }

            [NotNull, MaxLength(500)]
            public string Text { get; set; } = string.Empty;

            public double Lat { get; set; }
            public double Lng { get; set; }

            [NotNull]
            public byte[] EmbeddingBlob { get; set; } = Array.Empty<byte>();
        }

        public record SearchResult(string Text, double DistanceKm, float Score);
    }
}