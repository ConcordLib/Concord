using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public sealed class RewrittenFieldTarget {
}

public static class RewrittenProbe {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Untouched() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Overwritten() {
        return 2;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int PutBack() {
        return 3;
    }
}

[Collection(ForkFieldInjection.ApplyCollection)]
public sealed class RewrittenBodyTests : IDisposable {
    private const string NoInjection =
        "No field-injection runtime here, so nothing reports whether a body was rewritten.";

    private static readonly MethodInfo Untouched = typeof(RewrittenProbe).GetMethod(nameof(RewrittenProbe.Untouched))!;

    private static readonly MethodInfo Overwritten =
        typeof(RewrittenProbe).GetMethod(nameof(RewrittenProbe.Overwritten))!;

    private static readonly MethodInfo PutBack = typeof(RewrittenProbe).GetMethod(nameof(RewrittenProbe.PutBack))!;

    private readonly HashSet<MethodBase> written = [];

    public static bool Unavailable =>
        ForkRuntime.Unavailable || ForkInjection.Unavailable || ForkFieldInjection.Unavailable;

    public void Dispose() {
        foreach (MethodBase target in written) {
            ForkRuntime.Restore(target);
        }

        written.Clear();
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void AnUntouchedBodyIsNotReportedAsRewritten() {
        Assert.Equal(1, RewrittenProbe.Untouched());

        Assert.False(
            ForkInjection.HasRewrittenBody(Untouched),
            $"{nameof(ForkInjection.HasRewrittenBody)} reported a method nobody has written as rewritten, so the " +
            "verdict does not track body writes at all and neither of the other two facts here measures anything.");
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void AConcordBodyWriteIsNotCountedAsARewrittenBody() {
        ForkRuntime.Write(Overwritten, 5);
        written.Add(Overwritten);

        Assert.Equal(5, RewrittenProbe.Overwritten());

        Assert.False(
            ForkInjection.HasRewrittenBody(Overwritten),
            "ForkBodyWriter.Shared.Write replaced this body and the runtime counts it as rewritten. That means " +
            $"{nameof(ForkInjection.HasRewrittenBody)} answers true for every target Concord owns, so a stale-method " +
            "handler that refuses a rewritten body refuses all of them and regenerates nothing. The refusal rule " +
            "has to be rewritten around a verdict that separates Waylith.Injection's own splices from Concord's " +
            "writes.");
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void RestoringAConcordBodyWriteLeavesTheVerdictWhereAnUntouchedBodyStarts() {
        ForkRuntime.Write(PutBack, 7);
        written.Add(PutBack);
        Assert.Equal(7, RewrittenProbe.PutBack());

        ForkRuntime.Restore(PutBack);
        written.Remove(PutBack);

        Assert.Equal(3, RewrittenProbe.PutBack());

        Assert.False(
            ForkInjection.HasRewrittenBody(PutBack),
            "the declared body is back and the runtime still reports this method as rewritten. The verdict is a set " +
            "the host only ever adds to, so a restored target would stay flagged for the life of the process and a " +
            "stale-method handler keyed on it would refuse that target forever.");
    }

    [Fact(Skip = NoInjection, SkipWhen = nameof(Unavailable))]
    public void AnAccessorTheRuntimeBoundThroughProcessAssembliesReadsAsRewritten() {
        MethodInfo accessor = BuildInjectedFieldAccessor();

        ForkInjection.ProcessAssemblies([accessor.Module.Assembly]);

        Assert.True(
            ForkInjection.HasRewrittenBody(accessor),
            $"{nameof(ForkInjection.ProcessAssemblies)} ran the runtime's own InjectionProcessor.Finish over an " +
            "accessor this fact tagged with [InjectedField], and the verdict still read false for it. Finish is " +
            "the only place that writes the set behind the verdict, so a true reading here is what makes the " +
            "three false readings above mean something instead of a verdict that never turns true.");
    }

    private static MethodInfo BuildInjectedFieldAccessor() {
        AssemblyName name = new AssemblyName("ConcordRewrittenFieldProbe_" + Guid.NewGuid().ToString("N"));
        PersistedAssemblyBuilder assembly = new PersistedAssemblyBuilder(name, typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule(name.Name!);

        TypeBuilder accessors = module.DefineType(
            "Accessors",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class);

        MethodBuilder accessor = accessors.DefineMethod(
            "ReadInjectedField",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int),
            [typeof(RewrittenFieldTarget)]);
        accessor.SetCustomAttribute(ForkInjection.NewInjectedFieldAttribute());

        ILGenerator il = accessor.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        accessors.CreateType();

        using MemoryStream stream = new MemoryStream();
        assembly.Save(stream);
        Type built = Assembly.Load(stream.ToArray()).GetType("Accessors")!;
        return built.GetMethod("ReadInjectedField", BindingFlags.Public | BindingFlags.Static)!;
    }
}
