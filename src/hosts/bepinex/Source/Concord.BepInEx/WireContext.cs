using System;

namespace Concord.BepInEx;

internal sealed class WireContext {
    public Func<bool> InstallBackend;
    public Action<string> Log;
    public Action<string> LogError;
}
