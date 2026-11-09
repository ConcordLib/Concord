using Concord.Emit;

namespace Concord.MonoMod;

/// <summary>The install hook a host calls to give Concord an emit route on this runtime.</summary>
public static class MonoModEmit {
    /// <summary>
    ///     Installs this copy's MonoMod scratch generator. Core emits through the backend that
    ///     installed last, and an earlier copy's generator emits through that copy's resolve cache.
    /// </summary>
    public static void InstallScratchGenerator() {
        ScratchGenerator.Current = new MonoModScratchGenerator();
    }
}
