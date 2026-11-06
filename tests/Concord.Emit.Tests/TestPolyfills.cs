using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

internal sealed class MonoOnlyTheoryAttribute : TheoryAttribute {
    public MonoOnlyTheoryAttribute() {
        if (Type.GetType("Mono.Runtime") is null) {
            Skip = "mono is the only runtime where reflecting over a function-pointer type overflows the process stack, so the net472 fence has nothing to prove elsewhere.";
        }
    }
}

internal static class TestPolyfills {
    public static long GetAllocatedBytes() {
#if NET
        return GC.GetAllocatedBytesForCurrentThread();
#else
        return 0L;
#endif
    }

#if !NET
    public static TDelegate CreateDelegate<TDelegate>(this MethodInfo method)
        where TDelegate : Delegate {
        return (TDelegate)method.CreateDelegate(typeof(TDelegate));
    }
#endif
}
