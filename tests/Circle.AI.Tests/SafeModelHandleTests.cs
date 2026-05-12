using System;
using System.Runtime.InteropServices;
using Circle.AI.Core;
using Xunit;

namespace Circle.AI.Tests;

public sealed class SafeModelHandleTests
{
    // -----------------------------------------------------------------------
    // IsInvalid
    // -----------------------------------------------------------------------

    [Fact]
    public void DefaultConstructor_HandleIsInvalid()
    {
        using var h = new SafeModelHandle();
        Assert.True(h.IsInvalid);
    }

    [Fact]
    public void ConstructorWithNonZeroPointer_HandleIsNotInvalid()
    {
        var ptr = new IntPtr(0x1234);
        // Provide a no-op callback so Dispose doesn't crash.
        using var h = new SafeModelHandle(ptr, _ => { });
        Assert.False(h.IsInvalid);
    }

    [Fact]
    public void ConstructorWithZeroPointer_HandleIsInvalid()
    {
        using var h = new SafeModelHandle(IntPtr.Zero, _ => { });
        Assert.True(h.IsInvalid);
    }

    // -----------------------------------------------------------------------
    // Null callback guards
    // -----------------------------------------------------------------------

    [Fact]
    public void Constructor_NullReleaseCallback_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SafeModelHandle(new IntPtr(1), null!));
    }

    [Fact]
    public void WithReleaseCallback_NullCallback_ThrowsArgumentNullException()
    {
        using var h = new SafeModelHandle();
        Assert.Throws<ArgumentNullException>(() =>
            h.WithReleaseCallback(null!));
    }

    // -----------------------------------------------------------------------
    // Callback invocation on dispose
    // -----------------------------------------------------------------------

    [Fact]
    public void Dispose_InvokesReleaseCallback()
    {
        var called = false;
        var ptr = new IntPtr(0xABCD);

        var h = new SafeModelHandle(ptr, p =>
        {
            called = true;
            Assert.Equal(ptr, p);
        });

        h.Dispose();
        Assert.True(called);
    }

    [Fact]
    public void Dispose_ReleaseCallbackCalledExactlyOnce()
    {
        var callCount = 0;
        var h = new SafeModelHandle(new IntPtr(1), _ => callCount++);
        h.Dispose();
        // Second dispose must not trigger ReleaseHandle again.
        h.Dispose();
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void Dispose_ZeroHandle_CallbackNotInvoked()
    {
        // ReleaseHandle guards on handle != IntPtr.Zero.
        var called = false;
        var h = new SafeModelHandle(IntPtr.Zero, _ => called = true);
        h.Dispose();
        Assert.False(called);
    }

    // -----------------------------------------------------------------------
    // WithReleaseCallback fluent return
    // -----------------------------------------------------------------------

    [Fact]
    public void WithReleaseCallback_ReturnsSameInstance()
    {
        using var h = new SafeModelHandle();
        var ret = h.WithReleaseCallback(_ => { });
        Assert.Same(h, ret);
    }

    [Fact]
    public void WithReleaseCallback_IsInvokedOnDispose()
    {
        var h = new SafeModelHandle();
        var called = false;

        // Use the non-zero overload to set the handle, then assign callback.
        var h2 = new SafeModelHandle(new IntPtr(42), _ => { });
        h2.WithReleaseCallback(_ => called = true);
        h2.Dispose();

        Assert.True(called);
    }

    // -----------------------------------------------------------------------
    // IsClosed property (inherited from SafeHandle)
    // -----------------------------------------------------------------------

    [Fact]
    public void Dispose_IsClosed_BecomesTrue()
    {
        var h = new SafeModelHandle(new IntPtr(1), _ => { });
        h.Dispose();
        Assert.True(h.IsClosed);
    }
}
