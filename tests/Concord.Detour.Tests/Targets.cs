using System.Runtime.CompilerServices;

namespace Concord.Detour.Tests;

public static class Targets {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Original() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int OriginalC() {
        return 100;
    }
}
