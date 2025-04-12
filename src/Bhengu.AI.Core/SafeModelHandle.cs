// SafeModelHandle.cs
using System;
using System.Runtime.InteropServices;

namespace Bhengu.AI.Core
{
    public sealed class SafeModelHandle : SafeHandle
    {
        public SafeModelHandle() : base(IntPtr.Zero, true) { }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            if (!IsInvalid)
            {
                // Platform-specific cleanup
#if ANDROID
                    TfLiteModelFree(handle);
#elif IOS
                    CoreMLModelFree(handle);
#endif
            }
            return true;
        }

#if ANDROID
        [DllImport("libtensorflowlite")]
        private static extern void TfLiteModelFree(IntPtr handle);
#elif IOS
        [DllImport("__Internal")]
        private static extern void CoreMLModelFree(IntPtr handle);
#endif
    }
}