#if !NET
namespace System.Runtime.CompilerServices;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2094", Justification = "The compiler needs this type to exist on net472 to emit init accessors, and it needs no members.")]
internal static class IsExternalInit;
#endif
