using System.Runtime.CompilerServices;
using Concord.MonoMod;

namespace Concord.MonoMod.Tests;

internal static class ModuleSetup
{
    [ModuleInitializer]
    internal static void Init()
    {
        MonoModEmit.InstallScratchGenerator();
    }
}
