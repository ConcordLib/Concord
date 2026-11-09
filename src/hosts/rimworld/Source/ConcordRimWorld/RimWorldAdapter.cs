using System;
using Concord.Emit;
using Concord.Orchestration;
using Verse;

namespace Concord.RimWorld;

public static class RimWorldAdapter {
  private static bool wired;

  public static bool Ready { get; private set; }

  public static void Wire(ModContentPack content) {
    WireContext context = new WireContext {
      InstallBackend = () => BackendLoader.TryInstall(
          content.RootDir,
          content.assemblies?.loadedAssemblies,
          Say,
          message => Log.Error(message),
          BackendLoader.ModAssemblyDirectories),
      Log = message => Log.Warning(message),
      ApplyEagerTier = () => {
        RimWorldPatchApplier patchApplier = new RimWorldPatchApplier();

        RimWorldAttachedPropertyRegistry propertyRegistry = new RimWorldAttachedPropertyRegistry();
        RimWorldRuntime.Registry = propertyRegistry;
        PatchDeclarationScanner.ScanAssembly(typeof(RimWorldAdapter).Assembly, patchApplier, propertyRegistry, message => Log.Error(message));
        Patcher.UseAttachedPropertyRegistry(propertyRegistry);
      }
    };

    Wire(context);
  }

  internal static void Wire(WireContext context) {
    if (wired) {
      return;
    }

    wired = true;

    Patcher.UseLog(context.Log);

    if (!context.InstallBackend()) {
      return;
    }

    try {
      context.ApplyEagerTier();
      Ready = true;
    } catch {
      wired = false;
      throw;
    }
  }

  private static void Say(string message) {
    if (!UnityData.IsInMainThread) {
      LongEventHandler.ExecuteWhenFinished(() => Log.Message(message));
      return;
    }

    Log.Message(message);
  }

  internal static void ResetForTests() {
    wired = false;
    Ready = false;
  }
}

internal sealed class WireContext {
  public Func<bool> InstallBackend;
  public Action<string> Log;
  public Action ApplyEagerTier;
}
