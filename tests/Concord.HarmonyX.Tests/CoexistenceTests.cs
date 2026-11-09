using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.HarmonyX.Tests;

public static class CoexVictims {
    public static readonly List<string> Ran = [];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Inner(int value) {
        return value + 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Shared() {
        return Inner(1);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Later() {
        return Inner(1);
    }
}

public static class CoexPatches {
    public static void Prefix() {
        CoexVictims.Ran.Add("prefix");
    }

    public static void Postfix() {
        CoexVictims.Ran.Add("postfix");
    }

    public static void OnInvoke() {
        CoexVictims.Ran.Add("concord");
    }
}

public sealed class CoexistenceTests : IDisposable {
    private readonly List<string> lines = [];

    private readonly Action<string> logged = HarmonyXBackend.Log;

    private readonly List<Harmony> patchers = [];

    private readonly List<IBackendHandle> handles = [];

    public CoexistenceTests() {
        HarmonyXBackend.Log = line => lines.Add(line);
        HarmonyXEntryPoint.Install(line => lines.Add(line), null);
        CoexVictims.Ran.Clear();
    }

    public void Dispose() {
        foreach (IBackendHandle handle in handles) {
            handle.Dispose();
        }

        foreach (Harmony patcher in patchers) {
            patcher.UnpatchSelf();
        }

        HarmonyXBackend.Log = logged;
        CoexVictims.Ran.Clear();
    }

    [Fact]
    public void AHarmonyPrefixAndAConcordInvokeInjectionBothRun() {
        MethodInfo target = typeof(CoexVictims).GetMethod(nameof(CoexVictims.Shared))!;

        Harmony patcher = Patch(target, nameof(CoexPatches.Prefix), prefix: true, "coex.shared");
        IBackendHandle concord = Inject(target);

        Assert.True(concord.IsApplied, Lines());
        Assert.Equal(2, CoexVictims.Shared());
        Assert.Contains("prefix", CoexVictims.Ran);
        Assert.Contains("concord", CoexVictims.Ran);
        Assert.Contains(HarmonyXBackend.ForeignOwner, PatchBackend.Current.OwnersOf(target));
        Assert.Contains("coex.concord", PatchBackend.Current.OwnersOf(target));
        Assert.NotNull(patcher);
    }

    [Fact]
    public void AHarmonyPostfixAddedLaterLeavesConcordsInjectionRunning() {
        MethodInfo target = typeof(CoexVictims).GetMethod(nameof(CoexVictims.Later))!;

        IBackendHandle concord = Inject(target);
        Assert.True(concord.IsApplied, Lines());
        Assert.Equal(2, CoexVictims.Later());
        Assert.Contains("concord", CoexVictims.Ran);

        Patch(target, nameof(CoexPatches.Postfix), prefix: false, "coex.later");
        CoexVictims.Ran.Clear();

        Assert.Equal(2, CoexVictims.Later());
        Assert.Contains("concord", CoexVictims.Ran);
        Assert.Contains("postfix", CoexVictims.Ran);
        Assert.True(PatchBackend.Current.OwnsBody(target), Lines());
    }

    private IBackendHandle Inject(MethodBase target) {
        IBackendHandle handle = PatchBackend.Current.ApplyComposed(
            target,
            new[] {
                new Injection(
                    typeof(CoexPatches).GetMethod(nameof(CoexPatches.OnInvoke))!,
                    new InjectAt.Invoke(typeof(CoexVictims), nameof(CoexVictims.Inner), At.Head),
                    "coex.concord",
                    0),
            });

        handles.Add(handle);
        return handle;
    }

    private Harmony Patch(MethodBase target, string patch, bool prefix, string id) {
        Harmony patcher = new Harmony(id);
        patchers.Add(patcher);
        HarmonyMethod method = new HarmonyMethod(typeof(CoexPatches).GetMethod(patch)!);
        patcher.Patch(target, prefix ? method : null, prefix ? null : method);
        return patcher;
    }

    private string Lines() {
        return lines.Count == 0 ? "Nothing was logged." : "Logged: " + string.Join(" | ", lines);
    }
}
