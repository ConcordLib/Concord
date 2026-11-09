using System.Runtime.CompilerServices;
using Concord.MonoMod;

namespace Concord.Emit.Tests;

internal static class MonoModTestInit {
    [ModuleInitializer]
    internal static void Install() {
        MonoModEmit.InstallScratchGenerator();
    }
}
