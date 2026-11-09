using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.MonoMod.Harmony;

namespace Concord.MonoMod;

/// <summary>
///     What a host mod calls to install the detour backend. Call it once, before the first patch applies.
/// </summary>
public static class MonoModEntryPoint {
    private static readonly object Gate = new object();

    private static AssemblyLoadEventHandler? watcher;

    /// <summary>The choice a completed install made, or <c>null</c> before the first install.</summary>
    public static Selection? Current { get; private set; }

    /// <summary>Installs the backend and arms the Harmony coexistence bridge.</summary>
    /// <param name="log">Where to write the one install line. <c>null</c> sends it to <see cref="Trace" />.</param>
    /// <param name="assemblyDirectories">Directories to find a byte-loaded mod assembly's file in. Empty or <c>null</c> leaves [Local(Name)] unresolvable.</param>
    /// <returns>The outcome. Inspect <see cref="Selection.Installed" /> to see whether the backend is live.</returns>
    public static Selection Install(Action<string>? log, string[]? assemblyDirectories) {
        lock (Gate) {
            Action<string> write = log ?? (message => Trace.WriteLine(message));

            if (LoadedHarmony() is not null && UpdateWrapperHook.BindingIsReflectionOnly()) {
                Selection dead = new Selection(
                    false,
                    "this copy of the backend binds a 0Harmony that the host reloaded and left reflection-only, " +
                    "so it can arm no Harmony copy.",
                    true);
                write(dead.Message);
                return dead;
            }

            if (Current is { } already) {
                return already;
            }

            MonoModHost.Log = write;
            MonoModEmit.InstallScratchGenerator();
            RegisterLocalNameResolver(assemblyDirectories, write);

            RoutingBackend router = new RoutingBackend(new MonoModBackend(), write);
            PatchBackend.Current = router;
            ArmOrWatch(router, write);

            Selection chosen = new Selection(true, "detours through MonoMod.Core");
            write(chosen.Message);
            Current = chosen;
            return chosen;
        }
    }

    /// <summary>Forgets the installed selection. For tests, never for a running game.</summary>
    public static void ResetForTests() {
        lock (Gate) {
            if (watcher is not null) {
                AppDomain.CurrentDomain.AssemblyLoad -= watcher;
                watcher = null;
            }

            UpdateWrapperHook.StopWatchingForCopies();
            UpdateWrapperHook.DetachObserver();
            Current = null;
        }
    }

    private static void ArmOrWatch(RoutingBackend router, Action<string> write) {
        void ArmAttempt(Assembly loadedHarmony) {
            try {
                Arm(router, write);
            } catch (Exception ex) {
                write(HarmonyArrival.ArmFailedMarker + " " + loadedHarmony.FullName + ": " + ExceptionReason.Of(ex));
            }
        }

        watcher = (_, args) => {
            if (!HarmonyArrival.IsHarmony(args.LoadedAssembly)) {
                return;
            }

            ArmAttempt(args.LoadedAssembly);
        };

        AppDomain.CurrentDomain.AssemblyLoad += watcher;

        if (LoadedHarmony() is { } loaded) {
            ArmAttempt(loaded);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Arm(RoutingBackend router, Action<string> write) {
        router.ActivateHost(new HarmonyBridge(write));

        if (watcher is not null) {
            AppDomain.CurrentDomain.AssemblyLoad -= watcher;
            watcher = null;
        }
    }

    private static Assembly? LoadedHarmony() {
        foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies()) {
            if (HarmonyArrival.IsHarmony(loaded)) {
                return loaded;
            }
        }

        return null;
    }

    private static void RegisterLocalNameResolver(string[]? assemblyDirectories, Action<string> write) {
        if (assemblyDirectories is null || assemblyDirectories.Length == 0) {
            write(
                Selection.Prefix + "no assembly directories were supplied, so a mod assembly this host byte-loads " +
                "has no file on disk for Concord to read symbols or a method body from. [Local(Name = ...)] on a " +
                "target in one reports CONC151, and any check that needs the file cannot run.");
            return;
        }

        Patcher.UseLocalNameResolver(LocalNameResolver(assemblyDirectories));
    }

    private static Func<Module, string?> LocalNameResolver(string[] directories) {
        string[] copy = (string[])directories.Clone();

        return module => {
            foreach (string directory in copy) {
                string candidate = Path.Combine(directory, module.ScopeName);
                if (File.Exists(candidate)) {
                    return candidate;
                }
            }

            return null;
        };
    }
}
