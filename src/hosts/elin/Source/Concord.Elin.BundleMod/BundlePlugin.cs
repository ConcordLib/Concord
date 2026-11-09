using System.IO;
using System.Reflection;
using BepInEx;

namespace Concord.Elin.BundleMod;

[BepInPlugin("concordlib.concord.elin.bundlemod", "Concord Bundle Mod", "0.0.0")]
public sealed class BundlePlugin : BaseUnityPlugin {
    private void Awake() {
        string path = Path.Combine(Path.GetDirectoryName(typeof(BundlePlugin).Assembly.Location), "Concord.dll");
        Assembly.LoadFile(path);
        Logger.LogInfo("[Concord.Elin.BundleMod] loaded a second Concord.dll from " + path);
    }
}
