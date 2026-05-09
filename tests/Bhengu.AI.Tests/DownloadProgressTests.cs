using System;
using Bhengu.AI.Core;
using Xunit;

namespace Bhengu.AI.Tests;

// ============================================================================
// DownloadProgress  (the init-only snapshot returned to IProgress<T> consumers)
// ============================================================================

public sealed class DownloadProgressTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var p = new DownloadProgress();
        Assert.Equal(string.Empty,  p.FileName);
        Assert.Equal(0L,            p.BytesReceived);
        Assert.Equal(0L,            p.TotalBytes);
        Assert.Equal(0.0,           p.BytesPerSecond);
        Assert.Equal(TimeSpan.Zero, p.EstimatedTimeRemaining);
    }

    [Fact]
    public void InitProperties_SetCorrectly()
    {
        var eta = TimeSpan.FromSeconds(42);
        var p = new DownloadProgress
        {
            FileName                = "qwen3-14b.gguf",
            BytesReceived           = 512L,
            TotalBytes              = 8_000_000_000L,
            BytesPerSecond          = 104_857_600.0,  // 100 MB/s
            EstimatedTimeRemaining  = eta,
        };

        Assert.Equal("qwen3-14b.gguf",       p.FileName);
        Assert.Equal(512L,                    p.BytesReceived);
        Assert.Equal(8_000_000_000L,          p.TotalBytes);
        Assert.Equal(104_857_600.0,           p.BytesPerSecond);
        Assert.Equal(eta,                     p.EstimatedTimeRemaining);
    }

    [Fact]
    public void BytesReceived_LargeValue_HandledWithoutOverflow()
    {
        // Models can exceed 8 GB — verify long holds the value.
        var p = new DownloadProgress { BytesReceived = 8_500_000_000L };
        Assert.Equal(8_500_000_000L, p.BytesReceived);
    }

    [Fact]
    public void TotalBytes_NegativeOne_IndicatesUnknownLength()
    {
        // HTTP servers without Content-Length report -1; callers must handle it.
        var p = new DownloadProgress { TotalBytes = -1L };
        Assert.Equal(-1L, p.TotalBytes);
    }

    [Fact]
    public void BytesPerSecond_Zero_IndicatesNoThroughputYet()
    {
        var p = new DownloadProgress { BytesPerSecond = 0.0 };
        Assert.Equal(0.0, p.BytesPerSecond);
    }

    [Fact]
    public void EstimatedTimeRemaining_Zero_WhenDownloadComplete()
    {
        // When all bytes are received the ETA is zero.
        var p = new DownloadProgress
        {
            BytesReceived          = 1024L,
            TotalBytes             = 1024L,
            EstimatedTimeRemaining = TimeSpan.Zero,
        };
        Assert.Equal(TimeSpan.Zero, p.EstimatedTimeRemaining);
    }

    [Fact]
    public void FileName_EmptyString_IsDefaultAndValid()
    {
        var p = new DownloadProgress { FileName = "" };
        Assert.Equal("", p.FileName);
    }
}

// ============================================================================
// ModelDownloader.DownloadProgressReport
// (mutable class used by the event-based ProgressChanged delegate)
// ============================================================================

public sealed class DownloadProgressReportTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var r = new ModelDownloader.DownloadProgressReport();
        Assert.Equal(string.Empty,  r.FileName);
        Assert.Equal(0L,            r.BytesReceived);
        Assert.Equal(0L,            r.TotalBytes);
        Assert.Equal(0.0,           r.BytesPerSecond);
        Assert.Equal(TimeSpan.Zero, r.EstimatedTimeRemaining);
    }

    [Fact]
    public void Properties_SetViaAssignment_AreReflected()
    {
        var r = new ModelDownloader.DownloadProgressReport();
        r.FileName               = "model.gguf";
        r.BytesReceived          = 256L;
        r.TotalBytes             = 1024L;
        r.BytesPerSecond         = 50.0;
        r.EstimatedTimeRemaining = TimeSpan.FromSeconds(15);

        Assert.Equal("model.gguf",           r.FileName);
        Assert.Equal(256L,                   r.BytesReceived);
        Assert.Equal(1024L,                  r.TotalBytes);
        Assert.Equal(50.0,                   r.BytesPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(15), r.EstimatedTimeRemaining);
    }

    [Fact]
    public void Properties_CanBeOverwritten()
    {
        var r = new ModelDownloader.DownloadProgressReport
        {
            FileName      = "first.gguf",
            BytesReceived = 100L,
        };
        r.FileName      = "second.gguf";
        r.BytesReceived = 200L;

        Assert.Equal("second.gguf", r.FileName);
        Assert.Equal(200L,          r.BytesReceived);
    }

    // -----------------------------------------------------------------------
    // ProgressChanged event hookup (compile-time and runtime wiring check)
    // -----------------------------------------------------------------------

    [Fact]
    public void ProgressChangedDelegate_CanBeSubscribed()
    {
        var src = new FakeModelSource("fakehost");
        using var dl = new ModelDownloader(new[] { src });

        ModelDownloader.DownloadProgressReport? received = null;
        dl.ProgressChanged += r => received = r;

        // Without triggering an actual download we can't receive a report, but
        // the subscription itself must compile and not throw.
        Assert.Null(received); // no download happened → no event fired
    }

    [Fact]
    public void ProgressChangedDelegate_CanBeUnsubscribed()
    {
        var src = new FakeModelSource("fakehost");
        using var dl = new ModelDownloader(new[] { src });

        ModelDownloader.DownloadProgressHandler handler = _ => { };
        dl.ProgressChanged += handler;
        dl.ProgressChanged -= handler; // must not throw
    }

    [Fact]
    public void BytesReceived_LargeValue_HandledWithoutOverflow()
    {
        var r = new ModelDownloader.DownloadProgressReport { BytesReceived = 35_000_000_000L };
        Assert.Equal(35_000_000_000L, r.BytesReceived);
    }
}
