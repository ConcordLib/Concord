using System.Reflection;
using Concord.Emit;
using Xunit;

namespace Concord.Merged.Tests;

public static class MergedTargets {
    public static int Work() {
        int x = 5;
        try {
            x += 1;
        } finally {
            x += 1;
        }

        return x;
    }

    public static Control CancelWithValue(ControlHandle<int> ch) {
        ch.ReturnValue = 42;
        return Control.Cancel;
    }
}

public class MergedAssemblyTests {
    private static Assembly Merged => typeof(WrapperComposer).Assembly;

    [Fact]
    public void TheMergedAssemblyExportsConcordTypesOnly() {
        string[] leaked = Merged.GetExportedTypes()
            .Select(t => t.FullName ?? t.Name)
            .Where(n => !n.StartsWith("Concord", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(leaked.Length == 0, "exported non-Concord types: " + string.Join(", ", leaked));
    }

#if NET
    [Fact]
    public void AWrapperWithALocalAndAFinallyRegionRunsFromTheMergedAssembly() {
        MethodInfo? target = typeof(MergedTargets).GetMethod(nameof(MergedTargets.Work));
        Assert.NotNull(target);

        ComposeResult result = WrapperComposer.Compose(target!, Array.Empty<Injection>());

        Assert.Equal(7, Assert.IsType<int>(result.Wrapper.Invoke(null, Array.Empty<object>())));
    }

    [Fact]
    public void AHeadInjectionWithAControlHandleReturnValueRunsFromTheMergedAssembly() {
        MethodInfo? target = typeof(MergedTargets).GetMethod(nameof(MergedTargets.Work));
        MethodInfo? injection = typeof(MergedTargets).GetMethod(nameof(MergedTargets.CancelWithValue));
        Assert.NotNull(target);
        Assert.NotNull(injection);

        Injection head = new Injection(injection!, new InjectAt.Head(), "merged", 0);
        ComposeResult result = WrapperComposer.Compose(target!, new[] { head });

        Assert.Equal(42, Assert.IsType<int>(result.Wrapper.Invoke(null, Array.Empty<object>())));
    }
#endif

    [Fact]
    public void TheMergedAssemblyHasNoForeignPatchTypeInIt() {
        string[] namespaces = { "MonoMod", "HarmonyLib" };
        string[] foreign = Merged.GetTypes()
            .Where(t => t.Namespace != null && namespaces.Any(n => t.Namespace.StartsWith(n, StringComparison.Ordinal)))
            .Select(t => t.FullName ?? t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(foreign.Length == 0, "foreign patch types: " + string.Join(", ", foreign));
    }

    [Fact]
    public void TheMergedAssemblyReferencesTheFrameworkOnly() {
#if NET
        string[] expected = {
            "netstandard", "System.Collections", "System.Collections.Immutable", "System.Console",
            "System.Diagnostics.StackTrace", "System.Diagnostics.TraceSource", "System.Linq", "System.Memory",
            "System.Reflection.Emit.ILGeneration", "System.Reflection.Emit.Lightweight", "System.Reflection.Metadata",
            "System.Reflection.Primitives", "System.Runtime", "System.Runtime.InteropServices", "System.Threading"
        };
#else
        string[] expected = { "mscorlib", "System", "System.Core", "System.Numerics" };
#endif
        string[] unexpected = Merged.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => !expected.Contains(n, StringComparer.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(unexpected.Length == 0, "non-framework references: " + string.Join(", ", unexpected));
    }
}
