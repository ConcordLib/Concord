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

#if NET
    [Fact]
    public void TheNet10MergedAssemblyHasNoMonoModTypeInIt() {
        string[] monomod = Merged.GetTypes()
            .Where(t => t.Namespace?.StartsWith("MonoMod", StringComparison.Ordinal) == true)
            .Select(t => t.FullName ?? t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(monomod.Length == 0, "monomod types: " + string.Join(", ", monomod));
    }
#endif

#if !NET
    [Fact]
    public void TheNet472MergedAssemblyReferencesTheFrameworkOnly() {
        string[] expected = { "mscorlib", "System", "System.Core", "System.Numerics" };
        string[] unexpected = Merged.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => !expected.Contains(n, StringComparer.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(unexpected.Length == 0, "non-framework references: " + string.Join(", ", unexpected));
    }
#endif
}
