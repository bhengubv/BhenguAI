// In Circle.AI.Search project
using System;
using System.Numerics;

public static class VectorMath
{
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            throw new ArgumentException("Vectors must be same non-zero length");

        // Hardware-accelerated path — only when at least one full SIMD lane is available.
        if (Vector.IsHardwareAccelerated && a.Length >= Vector<float>.Count)
        {
            var dot = 0f;
            var normA = 0f;
            var normB = 0f;

            int i;
            for (i = 0; i <= a.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                var va = new Vector<float>(a.Slice(i));
                var vb = new Vector<float>(b.Slice(i));
                dot += Vector.Dot(va, vb);
                normA += Vector.Dot(va, va);
                normB += Vector.Dot(vb, vb);
            }

            // Scalar tail — remaining elements that didn't fill a full SIMD lane.
            for (; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }

            return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
        }

        // Fallback path (small vectors or no hardware SIMD).
        return CalculateCosineFallback(a, b);
    }

    private static float CalculateCosineFallback(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        float dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
    }
}