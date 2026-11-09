using System;
using System.Runtime.InteropServices;

namespace Concord.MonoMod.Tests
{
    internal static class TestRuntime
    {
        internal static bool IsNetFramework { get; } =
            RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework", StringComparison.Ordinal);
    }
}
