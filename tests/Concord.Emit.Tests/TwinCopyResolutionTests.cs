using System.Reflection;
using Concord.Backend;
using Xunit;

namespace Concord.Emit.Tests;

public static class TwinCopyTarget {
    public static int OriginCalls;

    public static int InjectionCalls;

    public static void OriginHelper() {
        OriginCalls++;
    }

    public static void InjectionHelper() {
        InjectionCalls++;
    }

    public static int Origin(int value) {
        OriginHelper();
        return value + 1;
    }

    public static void Head() {
        InjectionHelper();
    }
}

[Collection("LocalNames")]
public sealed class TwinCopyResolutionTests {
    [Fact]
    public void ComposedBody_InTheSecondCopy_CallsTheSecondCopysHelpers() {
        Type first = Load();
        Type second = Load();

        ComposeAndRun(second);

        Assert.Equal((1, 1), Counts(second));
        Assert.Equal((0, 0), Counts(first));
        Assert.Equal((0, 0), Counts(typeof(TwinCopyTarget)));
    }

    [Fact]
    public void ComposedBody_AfterTheFirstCopyComposed_StillCallsItsOwnCopy() {
        Type first = Load();
        Type second = Load();

        ComposeAndRun(first);
        ComposeAndRun(second);

        Assert.Equal((1, 1), Counts(first));
        Assert.Equal((1, 1), Counts(second));
        Assert.Equal((0, 0), Counts(typeof(TwinCopyTarget)));
    }

    [Fact]
    public void ComposedBody_WhoseFileImageIsAnotherBuild_CallsItsOwnCopy() {
        Type copy = Load();
        Module module = copy.Module;
        string image = typeof(TwinCopyTarget).Assembly.Location;
        Assert.NotEqual(module.ModuleVersionId, typeof(TwinCopyTarget).Module.ModuleVersionId);

        LocalNames.PathResolver = probed => probed == module ? image : null;
        try {
            ComposeAndRun(copy);
        } finally {
            LocalNames.PathResolver = null;
        }

        Assert.Equal((1, 1), Counts(copy));
        Assert.Equal((0, 0), Counts(typeof(TwinCopyTarget)));
    }

    private static Type Load() {
        byte[] image = File.ReadAllBytes(typeof(TwinCopyTarget).Assembly.Location);
        Assembly copy = Assembly.Load(AssemblyImage.WithFreshModuleId(image));
        Assert.NotSame(typeof(TwinCopyTarget).Assembly, copy);
        return copy.GetType(typeof(TwinCopyTarget).FullName!, true)!;
    }

    private static void ComposeAndRun(Type target) {
        MethodBase origin = target.GetMethod(nameof(TwinCopyTarget.Origin))!;
        MethodBase head = target.GetMethod(nameof(TwinCopyTarget.Head))!;
        ComposeResult result = WrapperComposer.Compose(origin, [new Injection(head, new InjectAt.Head(), "twin", 0)]);

        Assert.Equal(6, result.Wrapper.Invoke(null, [5]));
    }

    private static (int Origin, int Injection) Counts(Type target) {
        return (
            (int)target.GetField(nameof(TwinCopyTarget.OriginCalls))!.GetValue(null)!,
            (int)target.GetField(nameof(TwinCopyTarget.InjectionCalls))!.GetValue(null)!);
    }
}
