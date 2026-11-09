using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Concord.Backend;
using RimWorld;
using Verse;

namespace Concord.Waylith.RimProbe;

public static class Probe {
    private static readonly MethodBase SoloMethod = typeof(Solo).GetMethod(nameof(Solo.Value));

    private static readonly MethodBase GameMethod = typeof(GenMath).GetMethod(
        nameof(GenMath.PositiveMod), BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int), typeof(int) }, null);

    public static void Run(string modRootDir) {
        Report.Line("start runtime=" + RuntimeInformation.FrameworkDescription +
            " clr=" + Environment.Version +
            " game=" + VersionControl.CurrentVersionStringWithRev);

        Sentinel.Fill();

        Report.Step("env", () => Environment_(modRootDir));
        Report.Step("install", ReadBackend);
        Report.Step("solo", Solo_);
        Report.Step("game", Game_);
        Report.Step("hot", Hot_);
        Report.Step("frame", Frame_);
        Report.Step("registry", Registry);
        Report.Step("heap", Heap);

        Report.Line("measured");
        LongEventHandler.ExecuteWhenFinished(() => Report.Step("late", Late));
    }

    private static void Late() {
        string watch = Environment.GetEnvironmentVariable("CONCORD_RIMPROBE_WATCH") ?? string.Empty;
        Report.Line("late watch=" + (watch.Length == 0 ? "<none>" : watch) +
            " loaded=" + Lower(watch.Length != 0 && Find(watch) != null) +
            " assemblies=" + AppDomain.CurrentDomain.GetAssemblies().Length.ToString(CultureInfo.InvariantCulture));
        Report.Line("late solo=" + Solo.Value().ToString(CultureInfo.InvariantCulture) +
            " solo-replaced=" + Lower(Adapter.Replaced(SoloMethod)));
        Report.Line("lateframe " + (GameMethod == null ? "value=<absent>" :
            "value=" + GenMath.PositiveMod(9, 4).ToString(CultureInfo.InvariantCulture) +
            " runs=" + Volatile.Read(ref FramePatch.ConcordRuns).ToString(CultureInfo.InvariantCulture) +
            " published=" + Lower(Adapter.Replaced(GameMethod)) +
            " claimed=" + Oracle.Claimed(GameMethod)));
        Report.Line("lateheap heap-bad=" + Sentinel.Bad().ToString(CultureInfo.InvariantCulture));
        Report.Line("done");
    }

    private static void Environment_(string modRootDir) {
        Assembly concord = Find("Concord");

        Report.Line("env concord=" + Describe(concord) + " sha=" + Sha(concord));
        Report.Line("env adapter present=" + Lower(Adapter.Present) + " at=" + Adapter.Location);
        Report.Line("env mod-root=" + modRootDir);
        Report.Line("env backend-before=" + BackendShape());
        Report.Line("env patched-before=" + Patcher.PatchedTargetCount.ToString(CultureInfo.InvariantCulture));
    }

    private static void ReadBackend() {
        Report.Line("install patched-before=" +
            Patcher.PatchedTargetCount.ToString(CultureInfo.InvariantCulture));
        Report.Line("install selected=" + Adapter.Selected());
        Report.Line("install capability=" + Adapter.Support());
        Report.Line("install backend-after=" + BackendShape());
    }

    private static void Solo_() {
        Report.Line("solo before=" + Solo.Value().ToString(CultureInfo.InvariantCulture));
        Patcher.Patch(SoloMethod, Injection(nameof(Injections.SoloTail)), At.Tail);
        Report.Line("solo after=" + Solo.Value().ToString(CultureInfo.InvariantCulture) +
            " expected=" + Solo.Injected.ToString(CultureInfo.InvariantCulture) +
            " replaced=" + Lower(Adapter.Replaced(SoloMethod)) +
            " owners=" + Join(Adapter.Owners(SoloMethod)) +
            " concord-owners=" + Join(Patcher.OwnersOf(SoloMethod)));
    }

    private static void Game_() {
        if (GameMethod == null) {
            Report.Line("game MISSING Verse.GenMath.PositiveMod(int,int) is not on this build");
            return;
        }

        int before = GenMath.PositiveMod(7, 5);
        IPatchHandle handle = Patcher.Patch(GameMethod, Injection(nameof(Injections.GameTail)), At.Tail);
        int after = GenMath.PositiveMod(7, 5);
        bool replaced = Adapter.Replaced(GameMethod);
        string owners = Join(Adapter.Owners(GameMethod));
        handle.Dispose();
        int reverted = GenMath.PositiveMod(7, 5);

        Report.Line("game before=" + before.ToString(CultureInfo.InvariantCulture) +
            " after=" + after.ToString(CultureInfo.InvariantCulture) +
            " expected=" + Injections.GameInjected.ToString(CultureInfo.InvariantCulture) +
            " reverted=" + reverted.ToString(CultureInfo.InvariantCulture) +
            " replaced=" + Lower(replaced) +
            " owners=" + owners);
    }

    private static void Hot_() {
        if (GameMethod == null) {
            Report.Line("hot MISSING Verse.GenMath.PositiveMod(int,int) is not on this build");
            return;
        }

        Hot.Warm(400);
        Thread.Sleep(250);
        Hot.Warm(400);
        int throughBefore = Hot.Through();
        int directBefore = GenMath.PositiveMod(7, 5);

        IPatchHandle handle = Patcher.Patch(GameMethod, Injection(nameof(Injections.GameTail)), At.Tail);
        int throughAfter = Hot.Through();
        int directAfter = GenMath.PositiveMod(7, 5);
        bool published = Adapter.Replaced(GameMethod);
        Hot.Warm(400);
        Thread.Sleep(50);
        int throughLater = Hot.Through();
        handle.Dispose();
        int throughReverted = Hot.Through();

        Report.Line("hot warmed=" + Hot.Warmed.ToString(CultureInfo.InvariantCulture) +
            " through-before=" + throughBefore.ToString(CultureInfo.InvariantCulture) +
            " direct-before=" + directBefore.ToString(CultureInfo.InvariantCulture) +
            " through-after=" + throughAfter.ToString(CultureInfo.InvariantCulture) +
            " direct-after=" + directAfter.ToString(CultureInfo.InvariantCulture) +
            " through-later=" + throughLater.ToString(CultureInfo.InvariantCulture) +
            " through-reverted=" + throughReverted.ToString(CultureInfo.InvariantCulture) +
            " expected-patched=" + Injections.GameInjected.ToString(CultureInfo.InvariantCulture) +
            " expected-plain=2" +
            " published=" + Lower(published) +
            " caller-inlining=" + (Hot.Inlined() ? "possible" : "blocked"));
    }

    private static void Frame_() {
        if (GameMethod == null) {
            Report.Line("frame MISSING Verse.GenMath.PositiveMod(int,int) is not on this build");
            return;
        }

        Patcher.Patch(GameMethod, FrameInjection(nameof(FramePatch.ConcordTail)), At.Tail);
        int afterConcord = GenMath.PositiveMod(9, 4);

        Report.Line("frame value=" + afterConcord.ToString(CultureInfo.InvariantCulture) +
            " expected-value=1" +
            " published=" + Lower(Adapter.Replaced(GameMethod)) +
            " claimed=" + Oracle.Claimed(GameMethod) +
            " runs=" + Volatile.Read(ref FramePatch.ConcordRuns).ToString(CultureInfo.InvariantCulture) +
            " owners=" + Join(Adapter.Owners(GameMethod)) +
            " concord-owners=" + Join(Patcher.OwnersOf(GameMethod)));
        Report.Line("frameseen method=" + FramePatch.FrameMethod +
            " depth=" + FramePatch.FrameDepth.ToString(CultureInfo.InvariantCulture) +
            " offset=" + FramePatch.FrameOffset.ToString(CultureInfo.InvariantCulture) +
            " owner=" + FramePatch.FrameOwner +
            " stack=" + FramePatch.FrameStack);
    }

    private static void Registry() {
        Report.Line("registry-counts " + Oracle.RegistryCounts());
        Report.Line("registry-claims " + Oracle.ClaimedTargets());
        Report.Line("registry-summary " + Oracle.Summary());
    }

    private static void Heap() {
        int badBeforeGc = Sentinel.Bad();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);

        Report.Line("heap blocks=" + Sentinel.Blocks.ToString(CultureInfo.InvariantCulture) +
            " bytes=" + Sentinel.Bytes.ToString(CultureInfo.InvariantCulture) +
            " bad-before-gc=" + badBeforeGc.ToString(CultureInfo.InvariantCulture) +
            " bad=" + Sentinel.Bad().ToString(CultureInfo.InvariantCulture) +
            " compacting-gc=forced" +
            " gen2-collections=" + GC.CollectionCount(2).ToString(CultureInfo.InvariantCulture));
    }

    private static MethodBase Injection(string name) {
        return typeof(Injections).GetMethod(name, BindingFlags.Public | BindingFlags.Static);
    }

    private static MethodBase FrameInjection(string name) {
        return typeof(FramePatch).GetMethod(name, BindingFlags.Public | BindingFlags.Static);
    }

    private static string BackendShape() {
        try {
            return PatchBackend.Current.GetType().FullName;
        } catch (InvalidOperationException) {
            return "<none>";
        }
    }

    private static Assembly Find(string name) {
        foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies()) {
            if (SimpleName(loaded) == name) {
                return loaded;
            }
        }

        return null;
    }

    private static string SimpleName(Assembly assembly) {
        string full = assembly.FullName;
        if (full == null) {
            return null;
        }

        int comma = full.IndexOf(',');
        return comma < 0 ? full : full.Substring(0, comma);
    }

    private static string Describe(Assembly assembly) {
        if (assembly == null) {
            return "<absent>";
        }

        string location;
        try {
            location = string.IsNullOrEmpty(assembly.Location) ? "<memory>" : assembly.Location;
        } catch (Exception) {
            location = "<memory>";
        }

        return Report.Flatten(assembly.FullName) + " at=" + location;
    }

    private static string Sha(Assembly assembly) {
        AssemblyInformationalVersionAttribute stamp = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (stamp?.InformationalVersion == null) {
            return "<none>";
        }

        int plus = stamp.InformationalVersion.IndexOf('+');
        return plus < 0 ? "<none>" : stamp.InformationalVersion.Substring(plus + 1);
    }

    private static string Join(IReadOnlyList<string> values) {
        return values == null || values.Count == 0 ? "<empty>" : string.Join("/", values);
    }

    private static string Lower(bool value) {
        return value ? "true" : "false";
    }
}

public static class Hot {
    private static int warmed;

    public static int Warmed => warmed;

    public static int Through() {
        return GenMath.PositiveMod(7, 5);
    }

    public static int Warm(int times) {
        int accumulated = 0;
        for (int i = 0; i < times; i++) {
            accumulated += Through();
        }

        warmed += times;
        return accumulated;
    }

    public static bool Inlined() {
        MethodBase target = typeof(GenMath).GetMethod(
            nameof(GenMath.PositiveMod), BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int), typeof(int) }, null);
        return target != null && (target.GetMethodImplementationFlags() & MethodImplAttributes.NoInlining) == 0;
    }
}

public static class FramePatch {
    public static int ConcordRuns;

    public static string FrameMethod = "<unseen>";

    public static string FrameOwner = "<unseen>";

    public static string FrameStack = "<unseen>";

    public static int FrameDepth = -1;

    public static int FrameOffset = -1;

    public static void ConcordTail(ControlHandle<int> handle) {
        Interlocked.Increment(ref ConcordRuns);
        if (Volatile.Read(ref FrameDepth) < 0) {
            Capture();
        }
    }

    private static void Capture() {
        try {
            StackTrace trace = new StackTrace(false);
            List<string> walked = new List<string>();
            int depth = trace.FrameCount < 6 ? trace.FrameCount : 6;

            for (int i = 0; i < depth; i++) {
                StackFrame frame = trace.GetFrame(i);
                MethodBase method = frame?.GetMethod();
                if (method == null) {
                    walked.Add("<nomethod>");
                    continue;
                }

                string name = (method.DeclaringType == null ? "<module>" : method.DeclaringType.Name) + "." + method.Name;
                walked.Add(name);

                if (method.DeclaringType == typeof(GenMath) && method.Name == nameof(GenMath.PositiveMod)) {
                    FrameMethod = name;
                    FrameOffset = frame.GetILOffset();
                    FrameOwner = Oracle.OwnerOfFrame(frame);
                    FrameStack = string.Join("|", walked.ToArray());
                    Volatile.Write(ref FrameDepth, i);
                    return;
                }
            }

            FrameMethod = "<nomatch>";
            FrameOwner = "<null>";
            FrameStack = walked.Count == 0 ? "<empty>" : string.Join("|", walked.ToArray());
            Volatile.Write(ref FrameDepth, 99);
        } catch (Exception thrown) {
            FrameMethod = "<threw>";
            FrameOwner = thrown.GetType().Name;
            FrameStack = "<threw>";
            Volatile.Write(ref FrameDepth, 98);
        }
    }
}

public static class Sentinel {
    private const int BlockCount = 2048;

    private const int BlockSize = 256;

    private static byte[][] blocks;

    public static int Blocks => blocks == null ? 0 : blocks.Length;

    public static long Bytes => (long)BlockCount * BlockSize;

    public static void Fill() {
        byte[][] built = new byte[BlockCount][];
        for (int i = 0; i < BlockCount; i++) {
            byte[] block = new byte[BlockSize];
            for (int j = 0; j < BlockSize; j++) {
                block[j] = Pattern(i, j);
            }

            built[i] = block;
        }

        blocks = built;
    }

    public static int Bad() {
        byte[][] held = blocks;
        if (held == null) {
            return -1;
        }

        int bad = 0;
        for (int i = 0; i < held.Length; i++) {
            byte[] block = held[i];
            if (block == null || block.Length != BlockSize) {
                bad++;
                continue;
            }

            for (int j = 0; j < BlockSize; j++) {
                if (block[j] != Pattern(i, j)) {
                    bad++;
                    break;
                }
            }
        }

        return bad;
    }

    private static byte Pattern(int block, int offset) {
        return (byte)(((block * 31) + (offset * 7) + 13) & 0xFF);
    }
}

public static class Oracle {
    private static readonly Assembly Assembly = Of("Concord.Waylith");

    private static readonly Type RegistryType = Assembly?.GetType("Concord.Waylith.WriterRegistry");

    private static readonly Type AttributionType = Assembly?.GetType("Concord.Waylith.FrameAttribution");

    public static string Claimed(MethodBase target) {
        if (target == null) {
            return "no-target";
        }

        try {
            return PatchBackend.Current.OwnsBody(target) ? "true" : "false";
        } catch (Exception thrown) {
            return "threw-" + thrown.GetType().Name;
        }
    }

    public static string OwnerOfFrame(StackFrame frame) {
        if (AttributionType == null || frame == null) {
            return "adapter-absent";
        }

        try {
            object owner = AttributionType
                .GetMethod("OwnerOfFrame", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { frame });
            return owner == null ? "<null>" : Report.Flatten((string)owner);
        } catch (Exception thrown) {
            return "threw-" + thrown.GetType().Name;
        }
    }

    public static string RegistryCounts() {
        if (RegistryType == null) {
            return "claims=adapter-absent published-bodies=adapter-absent published-bytes=adapter-absent";
        }

        try {
            object shared = Shared();
            return "claims=" + Property(shared, "ClaimCount") +
                " published-bodies=" + Property(shared, "PublishedBodies") +
                " published-bytes=" + Property(shared, "PublishedBytes");
        } catch (Exception thrown) {
            string threw = "threw-" + thrown.GetType().Name;
            return "claims=" + threw + " published-bodies=" + threw + " published-bytes=" + threw;
        }
    }

    public static string ClaimedTargets() {
        if (RegistryType == null) {
            return "targets=adapter-absent";
        }

        try {
            List<string> named = new List<string>();
            foreach (object record in Claims(Shared())) {
                MethodBase target = (MethodBase)Property(record, "Target");
                named.Add((target.DeclaringType == null ? target.Module.Name : target.DeclaringType.FullName) + "." + target.Name);
            }

            named.Sort(StringComparer.Ordinal);
            return "targets=" + (named.Count == 0 ? "<none>" : string.Join("|", named.ToArray()));
        } catch (Exception thrown) {
            return "targets=threw-" + thrown.GetType().Name;
        }
    }

    public static string Summary() {
        if (RegistryType == null) {
            return "adapter-absent";
        }

        try {
            return Report.Flatten((string)RegistryType
                .GetMethod("Summary", BindingFlags.Public | BindingFlags.Instance)
                .Invoke(Shared(), null));
        } catch (Exception thrown) {
            return "threw-" + thrown.GetType().Name;
        }
    }

    private static IEnumerable<object> Claims(object shared) {
        return (IEnumerable<object>)RegistryType
            .GetMethod("Claims", BindingFlags.Public | BindingFlags.Instance)
            .Invoke(shared, null);
    }

    private static object Property(object holder, string name) {
        return holder.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance).GetValue(holder);
    }

    private static object Shared() {
        return RegistryType.GetProperty("Shared", BindingFlags.Public | BindingFlags.Static).GetValue(null);
    }

    private static Assembly Of(string name) {
        foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies()) {
            string full = loaded.FullName;
            if (full == null) {
                continue;
            }

            int comma = full.IndexOf(',');
            if ((comma < 0 ? full : full.Substring(0, comma)) == name) {
                return loaded;
            }
        }

        return null;
    }
}
