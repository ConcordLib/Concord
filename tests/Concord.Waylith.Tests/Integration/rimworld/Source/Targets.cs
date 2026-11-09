using System.Runtime.CompilerServices;

namespace Concord.Waylith.RimProbe;

public static class Solo {
    public const int Original = 1;

    public const int Injected = 11;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Value() {
        return Original;
    }
}

public static class Injections {
    public const int GameInjected = 42;

    public static void SoloTail(ControlHandle<int> handle) {
        handle.ReturnValue = Solo.Injected;
    }

    public static void GameTail(ControlHandle<int> handle) {
        handle.ReturnValue = GameInjected;
    }
}
