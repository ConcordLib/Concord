using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.Emit;
using HarmonyLib;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Xunit;

namespace Concord.HarmonyX.Tests;

public static class RearmVictim {
    public static readonly List<string> Trace = [];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Failed() {
        Trace.Add("body");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Recovered() {
        Trace.Add("body");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void KeepsEarlier() {
        Trace.Add("body");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void KeepsPrefix() {
        Trace.Add("body");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RefusedTwice() {
        Trace.Add("body");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RemovedUnderRefusal() {
        Trace.Add("body");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ScopesRefused() {
        Trace.Add("body");
    }

    public static void Note() {
        Trace.Add("concord");
    }

    public static void NoteA() {
        Trace.Add("a");
    }

    public static void NoteB() {
        Trace.Add("b");
    }

    public static void NoteC() {
        Trace.Add("c");
    }

    public static void Prefix() {
        Trace.Add("prefix");
    }
}

public sealed class RearmFailureTests : IDisposable {
    private readonly Action<string> logged = HarmonyXBackend.Log;

    private readonly List<IBackendHandle> handles = [];

    private readonly List<ILHook> foreign = [];

    private readonly List<Harmony> patchers = [];

    private readonly HashSet<int> seen = [];

    private Func<int, bool> refuses = _ => false;

    private int original;

    public RearmFailureTests() {
        HarmonyXBackend.Log = _ => { };
        HarmonyXEntryPoint.Install(_ => { }, null);
    }

    public void Dispose() {
        refuses = _ => false;
        foreach (IBackendHandle handle in handles) {
            handle.Dispose();
        }

        foreach (Harmony patcher in patchers) {
            patcher.UnpatchSelf();
        }

        foreach (ILHook hook in foreign) {
            hook.Dispose();
        }

        HarmonyXBackend.Log = logged;
        RearmVictim.Trace.Clear();
    }

    [Fact]
    public void AFailedRearmLeavesNoConcordHookOnTheTarget() {
        MethodInfo target = Victim(nameof(RearmVictim.Failed));
        Foreign(target);

        refuses = RefusesComposed;
        Assert.ThrowsAny<Exception>(() => Arm(target));

        refuses = _ => false;
        Assert.Equal(0, ConcordHooksOn(target));
        Assert.Equal("body", Run(target));
    }

    [Fact]
    public void ARearmAfterTheForeignManipulatorRecoversAppliesOneScopesHookAndRunsTheInjection() {
        MethodInfo target = Victim(nameof(RearmVictim.Recovered));
        Foreign(target);

        refuses = RefusesComposed;
        Assert.ThrowsAny<Exception>(() => Arm(target));

        refuses = _ => false;
        IBackendHandle handle = Arm(target);

        Assert.True(handle.IsApplied);
        Assert.Equal(2, ConcordHooksOn(target));
        Assert.Equal("concord,body", Run(target));

        handle.Dispose();
        Assert.Equal(0, ConcordHooksOn(target));
        Assert.Equal("body", Run(target));
    }

    [Fact]
    public void AnAddTheForeignManipulatorRefuses_LeavesTheEarlierInjectionRunningAndNeverRunsTheRefusedOne() {
        MethodInfo target = Victim(nameof(RearmVictim.KeepsEarlier));
        Foreign(target);
        IBackendHandle a = Arm(target, nameof(RearmVictim.NoteA));

        refuses = RefusesUnseen;
        Assert.ThrowsAny<Exception>(() => Arm(target, nameof(RearmVictim.NoteB)));

        Assert.True(a.IsApplied);
        Assert.Equal(2, ConcordHooksOn(target));
        Assert.Equal("a,body", Run(target));

        refuses = _ => false;
        IBackendHandle c = Arm(target, nameof(RearmVictim.NoteC));

        Assert.True(a.IsApplied);
        Assert.True(c.IsApplied);
        Assert.Equal("c,a,body", Run(target));
    }

    [Fact]
    public void AnAddTheForeignManipulatorRefuses_LeavesAnEarlierHarmonyXPrefixRunning() {
        MethodInfo target = Victim(nameof(RearmVictim.KeepsPrefix));
        Foreign(target);
        Prefix(target);
        IBackendHandle a = Arm(target, nameof(RearmVictim.NoteA));
        Assert.Equal("prefix,a,body", Run(target));

        refuses = RefusesUnseen;
        Assert.ThrowsAny<Exception>(() => Arm(target, nameof(RearmVictim.NoteB)));

        Assert.True(a.IsApplied);
        Assert.Equal("prefix,a,body", Run(target));
    }

    [Fact]
    public void AnAddTheChainRefusesWithTheSurvivorsToo_DropsEveryConcordHookAndReportsTheEarlierHandleNotApplied() {
        MethodInfo target = Victim(nameof(RearmVictim.RefusedTwice));
        Foreign(target);
        IBackendHandle a = Arm(target, nameof(RearmVictim.NoteA));

        refuses = _ => true;
        Assert.ThrowsAny<Exception>(() => Arm(target, nameof(RearmVictim.NoteB)));

        Assert.False(a.IsApplied);
        Assert.Equal(0, ConcordHooksOn(target));

        refuses = _ => false;
        Assert.Null(Record.Exception(a.Dispose));
        Assert.Equal(0, ConcordHooksOn(target));
    }

    [Fact]
    public void ARemoveTheForeignManipulatorRefuses_DropsEveryConcordHookAndReportsTheSurvivorNotApplied() {
        MethodInfo target = Victim(nameof(RearmVictim.RemovedUnderRefusal));
        Foreign(target);
        IBackendHandle a = Arm(target, nameof(RearmVictim.NoteA));
        IBackendHandle b = Arm(target, nameof(RearmVictim.NoteB));

        refuses = RefusesComposed;
        Assert.ThrowsAny<Exception>(b.Dispose);

        Assert.False(a.IsApplied);
        Assert.Equal(0, ConcordHooksOn(target));
        Assert.Equal("body", Run(target));
    }

    [Fact]
    public void AFirstArmTheChainRefusesWhileTheScopesHookApplies_LeavesNoConcordHookAndASecondTryAddsNone() {
        MethodInfo target = Victim(nameof(RearmVictim.ScopesRefused));
        Foreign(target);

        refuses = _ => true;
        Assert.ThrowsAny<Exception>(() => Arm(target));
        Assert.Equal(0, ConcordHooksOn(target));

        Assert.ThrowsAny<Exception>(() => Arm(target));
        Assert.Equal(0, ConcordHooksOn(target));
    }

    private static MethodInfo Victim(string name) {
        return typeof(RearmVictim).GetMethod(name)!;
    }

    private bool RefusesComposed(int count) {
        return count != original;
    }

    private bool RefusesUnseen(int count) {
        return !seen.Contains(count);
    }

    private void Foreign(MethodBase target) {
        ILHook hook = new ILHook(target, il => {
            int count = il.Instrs.Count;
            if (original == 0) {
                original = count;
            }

            if (refuses(count)) {
                throw new InvalidOperationException("foreign manipulator refuses the composed body");
            }

            seen.Add(count);
        });

        foreign.Add(hook);
    }

    private void Prefix(MethodBase target) {
        Harmony patcher = new Harmony("rearm-failure.prefix." + Guid.NewGuid().ToString("N"));
        patchers.Add(patcher);
        patcher.Patch(target, new HarmonyMethod(typeof(RearmVictim).GetMethod(nameof(RearmVictim.Prefix))!));
    }

    private IBackendHandle Arm(MethodBase target, string note = nameof(RearmVictim.Note)) {
        IBackendHandle handle = PatchBackend.Current.ApplyComposed(
            target,
            new[] {
                new Injection(
                    typeof(RearmVictim).GetMethod(note)!,
                    new InjectAt.Head(),
                    "rearm.concord",
                    0),
            });

        handles.Add(handle);
        return handle;
    }

    private static string Run(MethodInfo target) {
        RearmVictim.Trace.Clear();
        target.Invoke(null, null);
        return string.Join(",", RearmVictim.Trace.ToArray());
    }

    private static int ConcordHooksOn(MethodBase target) {
        FieldInfo map = typeof(ILHook).GetField("_Map", BindingFlags.NonPublic | BindingFlags.Static)!;
        int count = 0;
        foreach (object context in ((IDictionary)map.GetValue(null)!).Values) {
            FieldInfo chainField = context.GetType().GetField("Chain", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
            foreach (ILHook hook in (IEnumerable<ILHook>)chainField.GetValue(context)!) {
                if (hook.Method.Name == target.Name
                    && hook.Method.DeclaringType == target.DeclaringType
                    && (hook.ID == HarmonyXTarget.HookId || hook.ID == HarmonyXTarget.ScopesHookId)) {
                    count++;
                }
            }
        }

        return count;
    }
}
