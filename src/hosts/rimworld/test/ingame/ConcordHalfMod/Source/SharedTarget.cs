using System.Runtime.CompilerServices;

namespace Concord.RimWorld.CoexTest;

public static class SharedTarget {
    public static int HeadCount;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Compute(int value) {
        return value + 1;
    }
}
