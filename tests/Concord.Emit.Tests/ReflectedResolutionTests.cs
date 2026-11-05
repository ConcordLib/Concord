using System.Reflection;
using Mono.Cecil;
using Xunit;

namespace Concord.Emit.Tests;

public static class ReflectedOverloadTargets {
    public static void Look<T>(ref T value, string label) {
        _ = value;
        _ = label;
    }

    public static void Look<T>(ref Box<T> value, string label) {
        _ = value;
        _ = label;
    }

    public static void Format<T>(T value, string label) {
        _ = value;
        _ = label;
    }

    public static void Format<T>(T value, int width) {
        _ = value;
        _ = width;
    }
}

public class ReflectedVirtualBase {
    public virtual string Describe() {
        return nameof(ReflectedVirtualBase);
    }
}

public class ReflectedVirtualDerived : ReflectedVirtualBase {
    public override string Describe() {
        return nameof(ReflectedVirtualDerived);
    }
}

public class ReflectedHiddenBase {
    public string Label() {
        return nameof(ReflectedHiddenBase);
    }
}

public class ReflectedHiddenDerived : ReflectedHiddenBase {
    public new string Label() {
        return nameof(ReflectedHiddenDerived);
    }
}

public class ReflectedInheritedOverloadBase {
    public string Pick(string value) {
        return value;
    }
}

public class ReflectedInheritedOverloadDerived : ReflectedInheritedOverloadBase {
    public string Pick(int value) {
        return value.ToString();
    }
}

public class ReflectedBothConstructors {
    internal static readonly string Tag;

    static ReflectedBothConstructors() {
        Tag = nameof(ReflectedBothConstructors);
    }

    public ReflectedBothConstructors() {
        Instance = Tag;
    }

    internal string Instance { get; }
}

public sealed class ReflectedResolutionTests {
    [Fact]
    public void EnumerableWhereResolvesPastItsIndexedOverload() {
        MethodInfo closed = typeof(Enumerable)
            .GetMethod(
                nameof(Enumerable.Where),
                1,
                [
                    typeof(IEnumerable<>).MakeGenericType(Type.MakeGenericMethodParameter(0)),
                    typeof(Func<,>).MakeGenericType(Type.MakeGenericMethodParameter(0), typeof(bool)),
                ])!
            .MakeGenericMethod(typeof(int));

        Assert.Equal<MethodBase>(closed, Imported(closed).ResolveReflection());
    }

    [Fact]
    public void EnumerableSelectResolvesPastItsIndexedOverload() {
        MethodInfo closed = typeof(Enumerable)
            .GetMethod(
                nameof(Enumerable.Select),
                2,
                [
                    typeof(IEnumerable<>).MakeGenericType(Type.MakeGenericMethodParameter(0)),
                    typeof(Func<,>).MakeGenericType(
                        Type.MakeGenericMethodParameter(0),
                        Type.MakeGenericMethodParameter(1)),
                ])!
            .MakeGenericMethod(typeof(int), typeof(string));

        Assert.Equal<MethodBase>(closed, Imported(closed).ResolveReflection());
    }

    [Fact]
    public void ARefGenericParameterPicksTheBareOverload() {
        MethodInfo closed = Look([Type.MakeGenericMethodParameter(0).MakeByRefType(), typeof(string)]);

        Assert.Equal<MethodBase>(closed, Imported(closed).ResolveReflection());
    }

    [Fact]
    public void ARefConstructedGenericParameterPicksTheBoxedOverload() {
        MethodInfo closed = Look([
            typeof(Box<>).MakeGenericType(Type.MakeGenericMethodParameter(0)).MakeByRefType(),
            typeof(string),
        ]);

        Assert.Equal<MethodBase>(closed, Imported(closed).ResolveReflection());
    }

    [Fact]
    public void TwoOverloadsSplitOnlyByTheirSecondParameterBothResolve() {
        MethodInfo labelled = Format(typeof(string));
        MethodInfo widthed = Format(typeof(int));

        Assert.Equal<MethodBase>(labelled, Imported(labelled).ResolveReflection());
        Assert.Equal<MethodBase>(widthed, Imported(widthed).ResolveReflection());
    }

    [Fact]
    public void AClosedGenericCarriedThroughASecondModuleStillResolves() {
        MethodInfo closed = typeof(Enumerable)
            .GetMethod(
                nameof(Enumerable.Where),
                1,
                [
                    typeof(IEnumerable<>).MakeGenericType(Type.MakeGenericMethodParameter(0)),
                    typeof(Func<,>).MakeGenericType(Type.MakeGenericMethodParameter(0), typeof(bool)),
                ])!
            .MakeGenericMethod(typeof(int));

        using ModuleDefinition first = ModuleDefinition.CreateModule("ReflectedFirst", ModuleKind.Dll);
        using ModuleDefinition second = ModuleDefinition.CreateModule("ReflectedSecond", ModuleKind.Dll);
        MethodReference inFirst = new RecordingReflectionImporter(first).ImportReference(closed, null);
        MethodReference inSecond = new RecordingMetadataImporter(second).ImportReference(inFirst, null);

        Assert.Equal<MethodBase>(closed, inSecond.ResolveReflection());
    }

    [Fact]
    public void AnOverrideAndItsBaseEachResolveToTheirOwnDeclaration() {
        MethodInfo derived = typeof(ReflectedVirtualDerived).GetMethod(nameof(ReflectedVirtualDerived.Describe))!;
        MethodInfo baseline = typeof(ReflectedVirtualBase).GetMethod(nameof(ReflectedVirtualBase.Describe))!;

        Assert.Equal<MethodBase>(derived, Imported(derived).ResolveReflection());
        Assert.Equal<MethodBase>(baseline, Imported(baseline).ResolveReflection());
    }

    [Fact]
    public void AnInstanceConstructorResolvesPastTheStaticOne() {
        ConstructorInfo instance = typeof(ReflectedBothConstructors).GetConstructor(Type.EmptyTypes)!;

        Assert.Equal<MethodBase>(instance, Imported(instance).ResolveReflection());
    }

    [Fact]
    public void AStaticConstructorResolvesPastTheInstanceOne() {
        ConstructorInfo initializer = typeof(ReflectedBothConstructors).TypeInitializer!;

        Assert.Equal<MethodBase>(initializer, Imported(initializer).ResolveReflection());
    }

    [Fact]
    public void AHiddenMethodResolvesToTheDerivedDeclaration() {
        MethodInfo derived = typeof(ReflectedHiddenDerived).GetMethod(nameof(ReflectedHiddenDerived.Label))!;

        Assert.Equal<MethodBase>(derived, Imported(derived).ResolveReflection());
    }

    [Fact]
    public void ABaseDeclaredOverloadStillResolvesUnderADerivedOverload() {
        MethodInfo inherited = typeof(ReflectedInheritedOverloadBase)
            .GetMethod(nameof(ReflectedInheritedOverloadBase.Pick), [typeof(string)])!;
        MethodInfo declared = typeof(ReflectedInheritedOverloadDerived)
            .GetMethod(nameof(ReflectedInheritedOverloadDerived.Pick), [typeof(int)])!;

        Assert.Equal<MethodBase>(inherited, Imported(inherited).ResolveReflection());
        Assert.Equal<MethodBase>(declared, Imported(declared).ResolveReflection());
    }

    private static MethodInfo Look(Type[] signature) {
        return typeof(ReflectedOverloadTargets)
            .GetMethod(nameof(ReflectedOverloadTargets.Look), 1, signature)!
            .MakeGenericMethod(typeof(int));
    }

    private static MethodInfo Format(Type second) {
        return typeof(ReflectedOverloadTargets)
            .GetMethod(
                nameof(ReflectedOverloadTargets.Format),
                1,
                [Type.MakeGenericMethodParameter(0), second])!
            .MakeGenericMethod(typeof(int));
    }

    private static MethodReference Imported(MethodBase method) {
        ModuleDefinition module = ModuleDefinition.CreateModule("ReflectedFresh", ModuleKind.Dll);
        return new DefaultReflectionImporter(module).ImportReference(method, null);
    }
}
