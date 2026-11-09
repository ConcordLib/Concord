using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.HarmonyX.Tests;

public static class ChainVictim {
    public static readonly List<string> Trace = [];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Work() {
        Trace.Add("body");
    }
}

public static class ChainInjections {
    public static void Note() {
        ChainVictim.Trace.Add("concord");
    }

    public static void HarmonyPrefix() {
        ChainVictim.Trace.Add("harmony");
    }

    public static void OtherPrefix() {
        ChainVictim.Trace.Add("other");
    }
}

public sealed class ChainOrderTests : IDisposable {
    private readonly List<string> lines = [];

    private readonly Action<string> logged = HarmonyXBackend.Log;

    private readonly List<Harmony> patchers = [];

    private readonly List<IBackendHandle> handles = [];

    public ChainOrderTests() {
        HarmonyXBackend.Log = line => lines.Add(line);
        HarmonyXEntryPoint.Install(line => lines.Add(line), null);
    }

    public void Dispose() {
        foreach (IBackendHandle handle in handles) {
            handle.Dispose();
        }

        foreach (Harmony patcher in patchers) {
            patcher.UnpatchSelf();
        }

        HarmonyXBackend.Log = logged;
        ChainVictim.Trace.Clear();
    }

    [Fact]
    public void ConcordSitsInsideHarmonysChainWhateverTheArmOrder() {
        MethodInfo target = typeof(ChainVictim).GetMethod(nameof(ChainVictim.Work))!;

        Assert.Equal("body", Run());

        IBackendHandle concord = Arm(target);
        Assert.True(concord.IsApplied, Lines());
        Assert.Equal("concord,body", Run());

        Harmony harmony = Patch(target, nameof(ChainInjections.HarmonyPrefix), "chain.harmony");
        Assert.Equal("harmony,concord,body", Run());

        Harmony other = Patch(target, nameof(ChainInjections.OtherPrefix), "chain.other");
        Assert.Equal("harmony,other,concord,body", Run());

        other.UnpatchSelf();
        Assert.Equal("harmony,concord,body", Run());

        concord.Dispose();
        Assert.Equal("harmony,body", Run());

        harmony.UnpatchSelf();
        Assert.Equal("body", Run());

        Patch(target, nameof(ChainInjections.HarmonyPrefix), "chain.reverse");
        Assert.Equal("harmony,body", Run());
        Arm(target);
        Assert.Equal("harmony,concord,body", Run());
    }

    private static string Run() {
        ChainVictim.Trace.Clear();
        ChainVictim.Work();
        return string.Join(",", ChainVictim.Trace.ToArray());
    }

    private IBackendHandle Arm(MethodBase target) {
        IBackendHandle handle = PatchBackend.Current.ApplyComposed(
            target,
            new[] {
                new Injection(
                    typeof(ChainInjections).GetMethod(nameof(ChainInjections.Note))!,
                    new InjectAt.Head(),
                    "chain.concord",
                    0),
            });

        handles.Add(handle);
        return handle;
    }

    private Harmony Patch(MethodBase target, string prefix, string id) {
        Harmony patcher = new Harmony(id);
        patchers.Add(patcher);
        patcher.Patch(target, new HarmonyMethod(typeof(ChainInjections).GetMethod(prefix)!));
        return patcher;
    }

    private string Lines() {
        return lines.Count == 0 ? "Nothing was logged." : "Logged: " + string.Join(" | ", lines);
    }
}
