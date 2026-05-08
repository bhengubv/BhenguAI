// AssemblyInfo.cs
//
// We disable runtime marshalling assembly-wide so the [LibraryImport]
// source-generated P/Invoke layer can pass blittable structs straight
// across the managed/native boundary with no runtime marshaller in the
// loop. This is required because llama.cpp's parameter structs include
// C++ bools and fixed-layout fields that the runtime marshaller treats
// as non-blittable.
//
// All native interop structs in this assembly must therefore be fully
// blittable: bools are stored as bytes, enums as 32-bit ints, and no
// [MarshalAs] attributes are applied to struct fields.
//
// We also tell the P/Invoke loader to look in the assembly directory
// (and the standard "safe" OS directories) for native libraries, which
// matches our deployment story: ship llama.dll / libllama.so /
// libllama.dylib alongside the .NET assembly.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: DisableRuntimeMarshalling]

[assembly: DefaultDllImportSearchPaths(
    DllImportSearchPath.AssemblyDirectory |
    DllImportSearchPath.SafeDirectories |
    DllImportSearchPath.UserDirectories)]

// Allow the test project to access internal members (e.g. BuildQwenChatPrompt).
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Bhengu.AI.Tests")]
