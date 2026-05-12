using System;
using System.IO;
using Xunit;

namespace Circle.AI.Tests;

/// <summary>
/// Tests for <see cref="PlatformInterop.LoadModel"/> argument guards.
/// The guard exceptions are thrown in managed code before any P/Invoke, so
/// these tests do not require the native llama.cpp library to be present.
/// </summary>
public sealed class PlatformInteropTests
{
    // ------------------------------------------------------------------
    // Argument guards — pure managed code, no native library needed
    // ------------------------------------------------------------------

    [Fact]
    public void LoadModel_NullPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => PlatformInterop.LoadModel(null!));
    }

    [Fact]
    public void LoadModel_EmptyPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => PlatformInterop.LoadModel(""));
    }

    [Fact]
    public void LoadModel_WhitespacePath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => PlatformInterop.LoadModel("   "));
    }

    [Fact]
    public void LoadModel_MissingFile_ThrowsFileNotFoundException()
    {
        // Path is valid but file doesn't exist — thrown before any P/Invoke.
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gguf");
        Assert.False(File.Exists(missingPath)); // pre-condition
        Assert.Throws<FileNotFoundException>(() => PlatformInterop.LoadModel(missingPath));
    }
}
