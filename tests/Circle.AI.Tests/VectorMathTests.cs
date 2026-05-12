using Xunit;

namespace Circle.AI.Tests;

/// <summary>
/// Tests for both <see cref="VectorMath"/> (Circle.AI.Search) and
/// <see cref="SimdOps"/> which share the same cosine-similarity logic.
/// </summary>
public sealed class VectorMathTests
{
    private const float Epsilon = 1e-5f;

    // ------------------------------------------------------------------
    // VectorMath
    // ------------------------------------------------------------------

    [Fact]
    public void VectorMath_IdenticalVectors_ReturnOne()
    {
        float[] v = { 1f, 2f, 3f };
        var result = VectorMath.CosineSimilarity(v, v);
        Assert.Equal(1f, result, precision: 4);
    }

    [Fact]
    public void VectorMath_OrthogonalVectors_ReturnZero()
    {
        float[] a = { 1f, 0f, 0f };
        float[] b = { 0f, 1f, 0f };
        var result = VectorMath.CosineSimilarity(a, b);
        Assert.Equal(0f, result, precision: 4);
    }

    [Fact]
    public void VectorMath_AntiParallel_ReturnNegativeOne()
    {
        float[] a = { 1f, 0f };
        float[] b = { -1f, 0f };
        var result = VectorMath.CosineSimilarity(a, b);
        Assert.Equal(-1f, result, precision: 4);
    }

    [Fact]
    public void VectorMath_KnownValues_CorrectResult()
    {
        // cos(45°) ≈ 0.7071
        float[] a = { 1f, 0f };
        float[] b = { 1f, 1f };
        var result = VectorMath.CosineSimilarity(a, b);
        Assert.InRange(result, 0.706f, 0.708f);
    }

    [Fact]
    public void VectorMath_DifferentLengths_Throws()
    {
        float[] a = { 1f, 2f };
        float[] b = { 1f };
        Assert.Throws<ArgumentException>(() => VectorMath.CosineSimilarity(a, b));
    }

    [Fact]
    public void VectorMath_EmptyVectors_Throws()
    {
        float[] empty = Array.Empty<float>();
        Assert.Throws<ArgumentException>(() => VectorMath.CosineSimilarity(empty, empty));
    }

    [Fact]
    public void VectorMath_SingleElement_Parallel()
    {
        float[] a = { 5f };
        float[] b = { 10f };
        var result = VectorMath.CosineSimilarity(a, b);
        Assert.Equal(1f, result, precision: 4);
    }

    [Fact]
    public void VectorMath_LargeVector_ConsistentResult()
    {
        // 512-dimensional unit vector along axis 0 — should produce 1.0
        const int dim = 512;
        var a = new float[dim];
        a[0] = 1f;
        var result = VectorMath.CosineSimilarity(a, a);
        Assert.InRange(result, 0.999f, 1.001f);
    }

    // ------------------------------------------------------------------
    // SimdOps (duplicates logic, same contract)
    // ------------------------------------------------------------------

    [Fact]
    public void SimdOps_IdenticalVectors_ReturnOne()
    {
        float[] v = { 1f, 2f, 3f };
        var result = SimdOps.CosineSimilarity(v, v);
        Assert.Equal(1f, result, precision: 4);
    }

    [Fact]
    public void SimdOps_OrthogonalVectors_ReturnZero()
    {
        float[] a = { 1f, 0f };
        float[] b = { 0f, 1f };
        var result = SimdOps.CosineSimilarity(a, b);
        Assert.Equal(0f, result, precision: 4);
    }

    [Fact]
    public void SimdOps_LargeVector_MatchesVectorMath()
    {
        const int dim = 256;
        var rng = new Random(42);
        var a = Enumerable.Range(0, dim).Select(_ => (float)rng.NextDouble()).ToArray();
        var b = Enumerable.Range(0, dim).Select(_ => (float)rng.NextDouble()).ToArray();

        var simd = SimdOps.CosineSimilarity(a, b);
        var vm   = VectorMath.CosineSimilarity(a, b);

        Assert.InRange(simd, vm - Epsilon, vm + Epsilon);
    }

    // ------------------------------------------------------------------
    // Edge cases: zero vectors and negative components
    // ------------------------------------------------------------------

    [Fact]
    public void VectorMath_AllZeroVector_ReturnsNaN()
    {
        // PRODUCTION RISK: if an embedding model returns an all-zero vector
        // (corrupt inference output), cosine similarity yields NaN (0/0).
        // Callers must guard against NaN before using the score.
        float[] zero = { 0f, 0f, 0f };
        float[] unit = { 1f, 0f, 0f };
        var result = VectorMath.CosineSimilarity(zero, unit);
        Assert.True(float.IsNaN(result),
            "Cosine similarity of a zero vector must be NaN (division by zero norm).");
    }

    [Fact]
    public void SimdOps_AllZeroVector_ReturnsNaN()
    {
        float[] zero = { 0f, 0f, 0f };
        float[] unit = { 0f, 1f, 0f };
        var result = SimdOps.CosineSimilarity(zero, unit);
        Assert.True(float.IsNaN(result),
            "Cosine similarity of a zero vector must be NaN (division by zero norm).");
    }

    [Fact]
    public void VectorMath_NegativeComponents_WorksCorrectly()
    {
        // Cosine similarity handles negative components correctly via dot product.
        float[] a = { 1f, -1f };
        float[] b = { -1f, 1f };
        // These are anti-parallel → similarity = -1.
        var result = VectorMath.CosineSimilarity(a, b);
        Assert.Equal(-1f, result, precision: 4);
    }

    [Fact]
    public void SimdOps_NegativeComponents_WorksCorrectly()
    {
        float[] a = { 1f, -1f };
        float[] b = { -1f, 1f };
        var result = SimdOps.CosineSimilarity(a, b);
        Assert.Equal(-1f, result, precision: 4);
    }

    [Fact]
    public void SimdOps_DifferentLengths_Throws()
    {
        float[] a = { 1f, 2f, 3f };
        float[] b = { 1f, 2f };
        Assert.Throws<ArgumentException>(() => SimdOps.CosineSimilarity(a, b));
    }

    [Fact]
    public void SimdOps_EmptyVectors_Throws()
    {
        float[] empty = Array.Empty<float>();
        Assert.Throws<ArgumentException>(() => SimdOps.CosineSimilarity(empty, empty));
    }

    [Fact]
    public void SimdOps_LargeVectorDifferentLength_Throws()
    {
        // Ensures the length guard triggers even when vectors are longer
        // than one SIMD lane (previously could access b[i] past its end).
        var a = new float[256];
        var b = new float[255];
        Assert.Throws<ArgumentException>(() => SimdOps.CosineSimilarity(a, b));
    }
}
