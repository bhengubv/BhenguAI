// SafeModelHandle.cs
//
// Kept as the public model-handle type used by older callers in
// Bhengu.AI.Core (e.g. PlatformInterop.LoadModel). With the llama.cpp
// pivot the on-device inference path lives in Bhengu.AI.Inference, which
// has its own internal LlamaModelHandle. This type now exists purely as a
// SafeHandle wrapper around an opaque <c>llama_model*</c> pointer that
// callers can pass back into PlatformInterop. The actual native free is
// delegated via a managed callback so Bhengu.AI.Core doesn't need a P/Invoke
// dependency on llama.cpp itself.

using System;
using System.Runtime.InteropServices;

namespace Bhengu.AI.Core
{
    /// <summary>
    /// SafeHandle wrapper around an opaque native model pointer (currently a
    /// <c>llama_model*</c> from llama.cpp). The release callback is supplied
    /// by the loader so this assembly stays free of native imports.
    /// </summary>
    public sealed class SafeModelHandle : SafeHandle
    {
        private Action<IntPtr>? _releaseCallback;

        /// <summary>
        /// Default constructor required by P/Invoke marshalling. The handle
        /// is invalid until the loader sets it via <see cref="SetHandle"/>
        /// and assigns a release callback via <see cref="WithReleaseCallback"/>.
        /// </summary>
        public SafeModelHandle() : base(IntPtr.Zero, ownsHandle: true) { }

        /// <summary>
        /// Constructs a wrapper around a known native pointer with an
        /// explicit release callback.
        /// </summary>
        public SafeModelHandle(IntPtr nativeHandle, Action<IntPtr> releaseCallback)
            : base(IntPtr.Zero, ownsHandle: true)
        {
            SetHandle(nativeHandle);
            _releaseCallback = releaseCallback ?? throw new ArgumentNullException(nameof(releaseCallback));
        }

        public override bool IsInvalid => handle == IntPtr.Zero;

        /// <summary>
        /// Wires up the release callback after construction. Used when the
        /// runtime constructs this handle via marshalling.
        /// </summary>
        public SafeModelHandle WithReleaseCallback(Action<IntPtr> releaseCallback)
        {
            _releaseCallback = releaseCallback ?? throw new ArgumentNullException(nameof(releaseCallback));
            return this;
        }

        protected override bool ReleaseHandle()
        {
            if (handle != IntPtr.Zero)
            {
                _releaseCallback?.Invoke(handle);
                SetHandle(IntPtr.Zero);
            }
            return true;
        }
    }
}
