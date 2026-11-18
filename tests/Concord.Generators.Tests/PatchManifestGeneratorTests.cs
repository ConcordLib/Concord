using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Concord.AttachedData;
using Concord.Backend;
using Concord.Emit;
using Concord.Orchestration;
using Concord.Tests.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Concord.Generators.Tests;

public sealed class PatchManifestGeneratorTests : IDisposable {
    private const string ManifestTypeName = "Concord.Generated.PatchManifest";

    private const string RegistryTypeName = "Concord.Generated.PatchRegistry";

    private const BindingFlags Declared = BindingFlags.Public |
                                          BindingFlags.NonPublic |
                                          BindingFlags.Instance |
                                          BindingFlags.Static |
                                          BindingFlags.DeclaredOnly;

    private const string KitchenSink = """
        [assembly: Concord.PatchRegistry(typeof(Mod.Registry))]

        namespace Game {
            [System.Flags]
            public enum Mode {
                None = 0,
                Read = 1,
                Write = 2,
                Both = Read | Write,
                Exec = 4,
            }

            public class Pawn {
                private int mood;

                public Pawn() { }

                public Pawn(int seed) {
                    mood = seed;
                }

                private int Mood { get => mood; set => mood = value; }

                public int this[int index] => mood + index;

                public int Tick(int seed) {
                    int doubled = seed * 2;
                    Fence.Open();
                    int recorded = Ledger.Record(doubled, "tick");
                    Receipt receipt = new Receipt(recorded);
                    Fence.Close();
                    return receipt.Amount + Recalculate(1) + Mood;
                }

                private int Recalculate(int add) {
                    return mood + add;
                }
            }

            public static class Ledger {
                public static int Record(int amount, string label) {
                    return amount;
                }
            }

            public static class Fence {
                public static void Open() { }

                public static void Close() { }
            }

            public sealed class Receipt {
                public Receipt(int amount) {
                    Amount = amount;
                }

                public int Amount { get; }
            }
        }

        namespace Mod {
            using System;
            using System.Collections.Generic;
            using Concord;

            public sealed class Registry : IPatchDeclarationProvider {
                public IReadOnlyList<Type> Declarations => Array.Empty<Type>();
            }

            public enum Extra {
                [EnumMember("mod.extra.one")]
                One,
            }

            [Patch(typeof(Game.Pawn))]
            [PatchBefore(typeof(PawnShadows))]
            [PatchBefore("Other.Mod")]
            [PatchAfter(typeof(Secret))]
            [PatchAfter("Third.Mod")]
            [PatchDebug]
            public static class PawnHooks {
                private const string Target = nameof(Game.Pawn.Tick);

                [EnumMember("mod.summer")]
                public static readonly Game.Mode Summer;

                private static int Gate {
                    [Inject(At.Head, Target)]
                    get => 0;
                }

                [Inject(At.Head, Target, Priority = -2, Body = PatchBody.StateMachine)]
                private static void Head() { }

                [Inject(Target, typeof(Game.Ledger), nameof(Game.Ledger.Record), At.Tail, 1, new[] { typeof(int) }, new[] { typeof(int), typeof(string) })]
                private static void AfterRecord([Capture(2)] string label) { }

                [InjectNew(Target, typeof(Game.Receipt), At.Head, 1)]
                [Slice(typeof(Game.Fence), nameof(Game.Fence.Open), 1, typeof(Game.Fence), nameof(Game.Fence.Close), 1)]
                private static void BeforeReceipt() { }

                [InjectNew(typeof(Game.Receipt), At.Tail)]
                [Slice(toType: typeof(Game.Fence), toMember: "Close")]
                private static void InConstructor() { }

                [Inject(Target, typeof(int), LocalAccess.Store, At.Local, 1, 0, -1, "doubled")]
                private static void OnDoubled([Local(Index = 1, Name = "q\"uote\\slash")] int value, [Bound] int seed) { }

                [Inject(Target, "a\"b\\c\td", At.Constant, 4000000000)]
                private static void OnText() { }

                [Inject(Target, '\'', At.Constant)]
                private static void OnChar() { }

                [Inject(Target, -7L, At.Constant)]
                private static void OnLong() { }

                [Inject(Target, 0.1f, At.Constant)]
                private static void OnFloat() { }

                [Inject(Target, -0.30000000000000004d, At.Constant)]
                private static void OnDouble() { }

                [Inject(Target, (byte)200, At.Constant)]
                private static void OnByte() { }

                [Inject(Target, (sbyte)-3, At.Constant)]
                private static void OnSByte() { }

                [Inject(Target, true, At.Constant)]
                private static void OnBool() { }

                [Inject(Target, Game.Mode.Read | Game.Mode.Exec, At.Constant)]
                private static void OnFlags() { }

                [Inject(Target, Game.Mode.Both, At.Constant)]
                private static void OnAlias() { }

                [Inject(Target, (Game.Mode)64, At.Constant)]
                private static void OnUnnamed() { }

                [Inject(Target, Hidden.Value, At.Constant)]
                private static void OnHiddenEnum() { }

                [Inject(Target, typeof(Secret), At.Constant)]
                private static void OnHiddenType() { }

                [Inject(Target, typeof(List<Secret>[]), At.Constant)]
                private static void OnHiddenComposite() { }

                [Inject(Target, typeof(Dictionary<,>), At.Constant)]
                private static void OnOpenGeneric() { }

                [Inject(At.Tail, new Type[0])]
                private static void OnConstructor() { }

                [Inject(At.Head, new[] { typeof(int) })]
                private static void OnSeededConstructor() { }

                [Inject(At.Head, (Type[])null)]
                private static void OnNullTypes() { }

                [Inject(0, At.Constant)]
                private static void OnConstructorConstant() { }

                private enum Hidden {
                    Value = 3,
                }

                private sealed class Secret { }
            }

            [Patch]
            [Shadow("mood")]
            [Shadow("Mood")]
            [Shadow("Recalculate", typeof(int))]
            public abstract partial class PawnShadows : Game.Pawn {
                [Attached]
                public static int Seen;

                [InjectField("mood")]
                private int fuel;

                [InjectField]
                private int moodCopy;

                protected PawnShadows([Bound] int seed) { }

                [InjectInstance]
                protected abstract Game.Pawn Self { get; }

                [InjectProperty("Mood")]
                protected abstract int Feeling { get; set; }

                [InjectProperty("Item")]
                protected abstract int this[int index] { get; }

                [InjectMethod("Recalculate")]
                protected abstract int Again(int add);

                [Inject(At.Head, nameof(Game.Pawn.Tick))]
                private void OnTick() {
                    fuel = moodCopy;
                }
            }
        }
        """;

    private const string RegistrySample = """
        namespace Game {
            public class Door {
                public void Open() { }
            }

            public class Window {
                public void Close() { }
            }
        }

        namespace Mod {
            using Concord;

            [Patch(typeof(Game.Door))]
            public static class Doors {
                [Inject(At.Head, "Open")]
                private static void OnOpen() { }
            }

            [Patch(typeof(Game.Window))]
            public static class Windows {
                [Inject(At.Head, "Close")]
                private static void OnClose() { }
            }

            public static class Outer {
                [Patch(typeof(Game.Door))]
                private static class Hidden {
                    [Inject(At.Tail, "Open")]
                    private static void OnOpen() { }
                }
            }
        }
        """;

    private readonly RecordingDetourBackend backend = new RecordingDetourBackend();
    private readonly List<string> logs = [];

    public PatchManifestGeneratorTests() {
        PatchBackend.Current = backend;
        Patcher.UseLog(logs.Add);
    }

    public void Dispose() {
        Patcher.UseLog(null);
    }

    [Fact]
    public void Build_WithoutTheSymbol_LeavesNoConcordAttributeInMetadata() {
        Assembly mod = Build(KitchenSink, false);

        Assert.Empty(ConcordAttributesInMetadata(mod));
    }

    [Fact]
    public void Build_WithoutTheSymbol_WritesTheManifestInItsFixedForm() {
        Type manifest = Build(KitchenSink, false).GetType(ManifestTypeName, true)!;

        Assert.True(manifest.IsAbstract && manifest.IsSealed);
        Assert.Equal(typeof(object), manifest.BaseType);
        Assert.Empty(manifest.GetInterfaces());
        Assert.Empty(manifest.GetFields(Declared));
        Assert.Empty(manifest.GetNestedTypes(Declared));
        Assert.Empty(manifest.GetProperties(Declared));
        Assert.Empty(manifest.GetCustomAttributesData());

        MethodInfo describe = Assert.Single(manifest.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Equal("Describe", describe.Name);
        Assert.Equal([typeof(object)], describe.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(typeof(void), describe.ReturnType);
        Assert.All(manifest.GetMethods(Declared), method => {
            Assert.True(method.IsStatic);
            Assert.Empty(method.GetCustomAttributesData());
            Assert.Equal(typeof(void), method.ReturnType);
            Assert.Equal([typeof(object)], method.GetParameters().Select(parameter => parameter.ParameterType));
        });
    }

    [Fact]
    public void Build_WithoutTheSymbol_HandsEachAttributeBackWithItsSourceArguments() {
        Assembly inMetadata = Build(KitchenSink, true);
        Assembly fromManifest = Build(KitchenSink, false);

        HashSet<string> seen = CompareEverywhere(inMetadata, fromManifest);

        Assert.Equal(18, seen.Count);
    }

    [Fact]
    public void Build_WithTheSymbol_HasTheManifestAsWellAsTheAttributesInMetadata() {
        Assembly mod = Build(KitchenSink, true);

        Assert.NotNull(mod.GetType(ManifestTypeName));
        Assert.NotEmpty(ConcordAttributesInMetadata(mod));
        CompareEverywhere(mod, mod);
    }

    [Fact]
    public void Build_ArgumentForms_ComeBackWithTheSameValues() {
        Assembly mod = Build(KitchenSink, false);
        Type hooks = mod.GetType("Mod.PawnHooks", true)!;

        Assert.Equal("a\"b\\c\td", ConstantOf(hooks, "OnText"));
        Assert.Equal('\'', ConstantOf(hooks, "OnChar"));
        Assert.Equal(-7L, ConstantOf(hooks, "OnLong"));
        Assert.Equal(0.1f, ConstantOf(hooks, "OnFloat"));
        Assert.Equal(-0.30000000000000004d, ConstantOf(hooks, "OnDouble"));
        Assert.Equal((byte)200, ConstantOf(hooks, "OnByte"));
        Assert.Equal((sbyte)-3, ConstantOf(hooks, "OnSByte"));
        Assert.Equal(true, ConstantOf(hooks, "OnBool"));
        Assert.Equal("Read, Exec", ConstantOf(hooks, "OnFlags")!.ToString());
        Assert.Equal(64, (int)ConstantOf(hooks, "OnUnnamed")!);
        Assert.Equal("Mod.PawnHooks+Hidden", ConstantOf(hooks, "OnHiddenEnum")!.GetType().FullName);
        Assert.Equal(hooks.GetNestedType("Secret", BindingFlags.NonPublic), ConstantOf(hooks, "OnHiddenType"));
        Assert.Equal(typeof(List<>).MakeGenericType(hooks.GetNestedType("Secret", BindingFlags.NonPublic)!).MakeArrayType(), ConstantOf(hooks, "OnHiddenComposite"));
        Assert.Equal(typeof(Dictionary<,>), ConstantOf(hooks, "OnOpenGeneric"));

        InjectAttribute head = AttributeStore.Get<InjectAttribute>(hooks.GetMethod("Head", Declared)!)!;
        Assert.Equal(-2, head.Priority);
        Assert.Equal(PatchBody.StateMachine, head.Body);
        Assert.Equal("Tick", head.Method);
        Assert.Empty(AttributeStore.Get<InjectAttribute>(hooks.GetMethod("OnConstructor", Declared)!)!.ParameterTypes!);
        Assert.Null(AttributeStore.Get<InjectAttribute>(hooks.GetMethod("OnNullTypes", Declared)!)!.ParameterTypes);

        LocalAttribute local = AttributeStore.Get<LocalAttribute>(hooks.GetMethod("OnDoubled", Declared)!.GetParameters()[0])!;
        Assert.Equal("q\"uote\\slash", local.Name);
        Assert.Equal(1, local.Index);
    }

    [Fact]
    public void Apply_PrivateNestedDeclarations_ApplyAndOrderByTheirLookedUpTypes() {
        const string source = """
            namespace Game {
                public class Door {
                    public void Open() { }
                }
            }

            namespace Mod {
                using Concord;

                public static class Outer {
                    [Patch(typeof(Game.Door))]
                    [PatchBefore(typeof(Sibling))]
                    private static class Hidden {
                        [Inject(At.Head, "Open")]
                        private static void OnOpen() { }
                    }

                    [Patch(typeof(Game.Door))]
                    private static class Sibling {
                        [Inject(At.Head, "Open")]
                        private static void OnOpen() { }
                    }
                }
            }
            """;
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Compile([("Mod.cs", source)], false);
        Assert.Empty(diagnostics);
        Assert.Contains(".Assembly.GetType(\"Mod.Outer+Sibling\", true)", GeneratorTestHarness.GeneratedSource(output, "ConcordPatchManifest"));
        Assembly mod = GeneratorTestHarness.Load(output);
        MethodBase open = mod.GetType("Game.Door", true)!.GetMethod("Open")!;

        using (Patcher.Apply(mod)) {
            Assert.Equal(["Mod.Outer+Hidden", "Mod.Outer+Sibling"], backend.OwnersOf(open).OrderBy(owner => owner, StringComparer.Ordinal));
            Injection hidden = Assert.Single(AddedOn(open), injection => injection.Owner == "Mod.Outer+Hidden");
            Assert.Equal(["Mod.Outer+Sibling"], hidden.BeforeOwners);
        }
    }

    [Fact]
    public void Apply_InjectionsOnOverloadsGenericAndByrefMethods_EachReachItsOwnTarget() {
        const string source = """
            namespace Game {
                public class Target {
                    public void A() { }

                    public void B() { }

                    public void C() { }

                    public void D() { }

                    public void E() { }

                    public void F() { }
                }
            }

            namespace Mod {
                using Concord;

                [Patch]
                public abstract unsafe class Overloads : Game.Target {
                    [Inject(At.Head, "A")]
                    private void On(int value) { }

                    [Inject(At.Head, "B")]
                    private void On(ref int value) { }

                    [Inject(At.Head, "C")]
                    private void On<T>(T value) { }

                    [Inject(At.Head, "D")]
                    private void On(string value) { }

                    [Inject(At.Head, "E")]
                    private void On(int* value) { }

                    [Inject(At.Head, "F")]
                    private void On(Hidden value) { }

                    private sealed class Hidden { }
                }
            }
            """;
        Assembly mod = Build(source, false);
        Type target = mod.GetType("Game.Target", true)!;
        Type overloads = mod.GetType("Mod.Overloads", true)!;
        Type hidden = overloads.GetNestedType("Hidden", BindingFlags.NonPublic)!;

        using (Patcher.Apply(mod)) {
            Assert.Equal(typeof(int), OnlyInjection(target, "A").GetParameters()[0].ParameterType);
            Assert.Equal(typeof(int).MakeByRefType(), OnlyInjection(target, "B").GetParameters()[0].ParameterType);
            Assert.True(OnlyInjection(target, "C").IsGenericMethodDefinition);
            Assert.Equal(typeof(string), OnlyInjection(target, "D").GetParameters()[0].ParameterType);
            Assert.Equal(typeof(int).MakePointerType(), OnlyInjection(target, "E").GetParameters()[0].ParameterType);
            Assert.Equal(hidden, OnlyInjection(target, "F").GetParameters()[0].ParameterType);
        }
    }

    [Fact]
    public void Apply_InjectionWithAFunctionPointerParameter_ReachesItsTarget() {
        const string source = """
            namespace Game {
                public class Target {
                    public void A() { }

                    public void B() { }
                }
            }

            namespace Mod {
                using Concord;

                [Patch]
                public abstract unsafe class Callbacks : Game.Target {
                    [Inject(At.Head, "A")]
                    private void On(delegate*<int, void> callback) { }

                    [Inject(At.Head, "B")]
                    private void On(int value) { }
                }
            }
            """;
        Assembly mod = Build(source, false);
        Type target = mod.GetType("Game.Target", true)!;

        using (Patcher.Apply(mod)) {
            Assert.True(OnlyInjection(target, "A").GetParameters()[0].ParameterType.IsFunctionPointer);
            Assert.Equal(typeof(int), OnlyInjection(target, "B").GetParameters()[0].ParameterType);
        }
    }

    [Fact]
    public void Apply_DeclarationNamingATypeThatIsNotInstalled_SkipsItWithTheLineOfTodayAndAppliesTheRest() {
        (Compilation absentOutput, _) = GeneratorTestHarness.Compile(
            [("Absent.cs", "namespace Absent { public class Target { public void Run() { } } }")], false, assemblyName: "NotInstalled_" + Guid.NewGuid().ToString("N"));
        MetadataReference absent = MetadataReference.CreateFromImage(GeneratorTestHarness.Emit(absentOutput));
        const string source = """
            namespace Game {
                public class Here {
                    public void Run() { }
                }
            }

            namespace Mod {
                using Concord;

                [Patch(typeof(Absent.Target))]
                public static class Ghost {
                    [Inject(At.Head, "Run")]
                    private static void OnRun() { }
                }

                [Patch(typeof(Game.Here))]
                public static class Live {
                    [Inject(At.Head, "Run")]
                    private static void OnRun() { }
                }
            }
            """;
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Compile([("Mod.cs", source)], false, [absent]);
        Assert.Empty(diagnostics);
        Assembly mod = GeneratorTestHarness.Load(output);
        MethodBase run = mod.GetType("Game.Here", true)!.GetMethod("Run")!;

        using (Patcher.Apply(mod)) {
            Assert.Equal(["Mod.Live"], backend.OwnersOf(run));
        }

        Assert.Contains(logs, line => line.Contains("skipped declaration Mod.Ghost") && line.Contains("not loaded"));
    }

    [Fact]
    public void Apply_InjectionWithAParameterTypeThatIsNotInstalled_SkipsThatDeclarationAndAppliesTheRest() {
        (Compilation absentOutput, _) = GeneratorTestHarness.Compile(
            [("Absent.cs", "namespace Absent { public class Target { public void Run() { } } }")], false, assemblyName: "NotInstalled_" + Guid.NewGuid().ToString("N"));
        MetadataReference absent = MetadataReference.CreateFromImage(GeneratorTestHarness.Emit(absentOutput));
        const string source = """
            namespace Game {
                public class Here {
                    public void Run() { }
                }
            }

            namespace Mod {
                using Concord;

                [Patch("Absent.Target")]
                public static class Ghost {
                    [Inject(At.Head, "Run")]
                    private static void OnRun(Absent.Target self) { }
                }

                [Patch(typeof(Game.Here))]
                public static class Live {
                    [Inject(At.Head, "Run")]
                    private static void OnRun() { }
                }
            }
            """;
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Compile([("Mod.cs", source)], false, [absent]);
        Assert.Empty(diagnostics);
        Assembly mod = GeneratorTestHarness.Load(output);
        MethodBase run = mod.GetType("Game.Here", true)!.GetMethod("Run")!;

        using (Patcher.Apply(mod)) {
            Assert.Equal(["Mod.Live"], backend.OwnersOf(run));
        }

        Assert.Contains(logs, line => line.Contains("skipped declaration Mod.Ghost") && line.Contains("not loaded"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Build_OnCSharp73_CompilesTheManifest(bool attributesInMetadata) {
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Compile(
            [("Mod.cs", KitchenSink)], attributesInMetadata, languageVersion: LanguageVersion.CSharp7_3);
        Assert.Empty(diagnostics);
        GeneratorTestHarness.AssertCompiles(output);
    }

    [Fact]
    public void Build_SameSourceInEitherFileOrder_WritesTheSameManifest() {
        (string, string) first = ("A.cs", """
            namespace Mod {
                using Concord;

                [Patch(typeof(string))]
                [PatchBefore("One")]
                [PatchBefore("Two")]
                public static partial class Split {
                    [Inject(At.Head, "Trim")]
                    private static void OnTrim() { }
                }
            }
            """);
        (string, string) second = ("B.cs", """
            namespace Mod {
                using Concord;

                [PatchBefore("Three")]
                public static partial class Split {
                    [Inject(At.Tail, "Trim")]
                    private static void AfterTrim() { }
                }

                [Patch(typeof(string))]
                public static class Other {
                    [Inject(At.Head, "Trim")]
                    private static void OnTrim() { }
                }
            }
            """);

        string forward = GeneratorTestHarness.GeneratedSource(GeneratorTestHarness.Compile([first, second], false).Output, "ConcordPatchManifest");
        string backward = GeneratorTestHarness.GeneratedSource(GeneratorTestHarness.Compile([second, first], false).Output, "ConcordPatchManifest");

        Assert.Equal(forward, backward);
    }

    [Fact]
    public void Build_WithoutAConcordAttribute_WritesNoManifest() {
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Compile(
            [("Plain.cs", "namespace Plain { public static class Helper { public static int One() { return 1; } } }")], false);

        Assert.Empty(diagnostics);
        Assert.DoesNotContain(output.SyntaxTrees, tree => tree.FilePath.Contains("ConcordPatchManifest"));
        Assert.Null(GeneratorTestHarness.Load(output).GetType(ManifestTypeName));
    }

    [Fact]
    public void Build_AttributeOnALocalFunction_IsACompileError() {
        const string source = """
            namespace Mod {
                using Concord;

                [Patch(typeof(string))]
                public static class Local {
                    private static void Host() {
                        [Inject(At.Head, "Trim")]
                        static void OnTrim() { }
                    }
                }
            }
            """;

        (_, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Compile([("Mod.cs", source)], false);

        Diagnostic error = Assert.Single(diagnostics);
        Assert.Equal("CONC187", error.Id);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Apply_PatchOnAFileLocalClass_AppliesIt(bool attributesInMetadata) {
        const string source = """
            namespace Game {
                public class Door {
                    public void Open() { }
                }
            }

            namespace Mod {
                using Concord;

                [Patch(typeof(Game.Door))]
                file static class Hooks {
                    [Inject(At.Head, "Open")]
                    private static void OnOpen() { }
                }
            }
            """;
        Assembly mod = Build(source, attributesInMetadata);
        Type hooks = Assert.Single(mod.GetTypes(), type => type.Name.EndsWith("__Hooks", StringComparison.Ordinal));
        MethodBase open = mod.GetType("Game.Door", true)!.GetMethod("Open")!;

        using (Patcher.Apply(mod)) {
            Assert.Equal([hooks.FullName!], backend.OwnersOf(open));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Apply_FileLocalClassesWithOneNameInTwoFiles_EachApplyToItsOwnTarget(bool attributesInMetadata) {
        (string, string) door = ("Door.cs", """
            namespace Game {
                public class Door {
                    public void Open() { }
                }
            }

            namespace Mod {
                using Concord;

                [Patch(typeof(Game.Door))]
                file static class Hooks {
                    [Inject(At.Head, "Open")]
                    private static void OnOpen() { }
                }
            }
            """);
        (string, string) window = ("Window.cs", """
            namespace Game {
                public class Window {
                    public void Open() { }
                }
            }

            namespace Mod {
                using Concord;

                [Patch(typeof(Game.Window))]
                file static class Hooks {
                    [Inject(At.Head, "Open")]
                    private static void OnOpen() { }
                }
            }
            """);
        Assembly mod = Build([door, window], attributesInMetadata);
        MethodBase openDoor = mod.GetType("Game.Door", true)!.GetMethod("Open")!;
        MethodBase openWindow = mod.GetType("Game.Window", true)!.GetMethod("Open")!;

        using (Patcher.Apply(mod)) {
            Assert.StartsWith("Mod.<Door>", Assert.Single(backend.OwnersOf(openDoor)), StringComparison.Ordinal);
            Assert.StartsWith("Mod.<Window>", Assert.Single(backend.OwnersOf(openWindow)), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Apply_FileLocalTypesInSignaturesAndArguments_ComeBackAsThoseTypes() {
        const string source = """
            namespace Game {
                public class Target {
                    public void A() { }

                    public void B() { }
                }
            }

            namespace Mod {
                using System.Collections.Generic;
                using Concord;

                file sealed class Secret { }

                [Patch]
                file abstract class Hooks : Game.Target {
                    [Inject(At.Head, "A")]
                    private void On(Secret value) { }

                    [Inject(At.Head, "B")]
                    private void On(List<Secret> value) { }
                }

                file static class Arguments {
                    [Inject("A", typeof(Secret), At.Constant)]
                    private static void OnSecret() { }

                    [Inject("A", typeof(List<Secret>[]), At.Constant)]
                    private static void OnSecrets() { }
                }
            }
            """;
        Assembly inMetadata = Build(source, true);
        Assembly fromManifest = Build(source, false);

        Assert.Contains("InjectAttribute", CompareEverywhere(inMetadata, fromManifest));
        foreach (Assembly mod in new[] { inMetadata, fromManifest }) {
            Type secret = Assert.Single(mod.GetTypes(), type => type.Name.EndsWith("__Secret", StringComparison.Ordinal));
            Type arguments = Assert.Single(mod.GetTypes(), type => type.Name.EndsWith("__Arguments", StringComparison.Ordinal));
            Type target = mod.GetType("Game.Target", true)!;
            Assert.Equal(secret, ConstantOf(arguments, "OnSecret"));
            Assert.Equal(typeof(List<>).MakeGenericType(secret).MakeArrayType(), ConstantOf(arguments, "OnSecrets"));

            using (Patcher.Apply(mod)) {
                Assert.Equal(secret, OnlyInjection(target, "A").GetParameters()[0].ParameterType);
                Assert.Equal(typeof(List<>).MakeGenericType(secret), OnlyInjection(target, "B").GetParameters()[0].ParameterType);
            }
        }
    }

    [Fact]
    public void Apply_FieldTargetedAttachedOnAnAutoProperty_RegistersTheBackingFieldInBothBuilds() {
        const string source = """
            namespace Game {
                public class Pawn {
                    public void Tick() { }
                }
            }

            namespace Mod {
                using Concord;

                [Patch]
                public abstract class Moods : Game.Pawn {
                    [field: Attached]
                    public int Seen { get; set; }

                    [Inject(At.Head, "Tick")]
                    private void OnTick() { }
                }
            }
            """;
        AttachedCalls registry = new AttachedCalls();
        Patcher.UseAttachedPropertyRegistry(registry);
        Assembly inMetadata = Build(source, true);
        Assembly fromManifest = Build(source, false);

        Assert.Contains("AttachedAttribute", CompareEverywhere(inMetadata, fromManifest));
        foreach (Assembly mod in new[] { inMetadata, fromManifest }) {
            Type moods = mod.GetType("Mod.Moods", true)!;
            Assert.NotNull(AttributeStore.Get<AttachedAttribute>(moods.GetField("<Seen>k__BackingField", Declared)!));

            using (Patcher.Apply(mod)) {
                Assert.Equal([("<Seen>k__BackingField", typeof(int))], registry.Calls.Where(call => call.Declaration == moods).Select(call => (call.Name, call.Value)));
            }
        }
    }

    [Fact]
    public void Build_AttributesOnAnotherTargetOfTheirDeclaration_ComeBackOnTheSameMember() {
        const string source = """
            namespace Game {
                public enum Mode {
                    None,
                }
            }

            namespace Mod {
                using System;
                using Concord;

                public abstract class Forms {
                    [field: Attached]
                    public int Seen { get; set; }

                    [field: InjectField("mood")]
                    public int Fuel { get => field; set => field = value; }

                    [field: EnumMember("mod.spring")]
                    public static Game.Mode Spring { get; }

                    public int Level { get => 0; [param: Bound] set { } }

                    public int Rank { get => 0; [param: Local(Index = 1)] init { } }

                    public event Action Moved { add { } [param: Capture(1)] remove { } }
                }

                public sealed record Settings([property: InjectProperty("Mood")] int Feeling, [field: InjectField("mood")] int Fuel, [param: Bound] int Seed);
            }
            """;

        HashSet<string> seen = CompareEverywhere(Build(source, true), Build(source, false));

        Assert.Superset(
            new HashSet<string> { "AttachedAttribute", "InjectFieldAttribute", "EnumMemberAttribute", "BoundAttribute", "LocalAttribute", "CaptureAttribute", "InjectPropertyAttribute" },
            seen);
    }

    [Fact]
    public void Build_FieldTargetedAttributeOnAFieldLikeEvent_IsACompileError() {
        const string source = """
            namespace Mod {
                using System;
                using Concord;

                [Patch(typeof(string))]
                public abstract class Hooks {
                    [field: Attached]
                    public event Action Changed;
                }
            }
            """;

        (_, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Compile([("Mod.cs", source)], false);

        Diagnostic error = Assert.Single(diagnostics);
        Assert.Equal("CONC187", error.Id);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("Mod.Hooks.Changed", error.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Apply_OverrideInADerivedClass_GetsThePatchAndTheInjectionOfItsBase() {
        const string source = """
            namespace Game {
                public class Door {
                    public void Open() { }
                }
            }

            namespace Mod {
                using Concord;

                [Patch]
                public abstract class BaseHooks : Game.Door {
                    [Inject(At.Head, "Open")]
                    protected virtual void OnOpen() { }
                }

                public class Hooks : BaseHooks {
                    protected override void OnOpen() { }
                }
            }
            """;
        Assembly inMetadata = Build(source, true);
        Assembly fromManifest = Build(source, false);

        Assert.Contains("PatchAttribute", CompareEverywhere(inMetadata, fromManifest));
        foreach (Assembly mod in new[] { inMetadata, fromManifest }) {
            MethodBase open = mod.GetType("Game.Door", true)!.GetMethod("Open")!;
            using (Patcher.Apply(mod)) {
                Injection hooks = Assert.Single(AddedOn(open), injection => injection.Owner == "Mod.Hooks");
                Assert.Equal(mod.GetType("Mod.Hooks", true)!.GetMethod("OnOpen", Declared), hooks.InjectionMethod);
            }
        }
    }

    [Fact]
    public void Apply_DerivedClassWhoseInheritedPatchNamesAnotherTarget_IsRefusedTheSameWayInBothBuilds() {
        const string source = """
            namespace Game {
                public class Door {
                    public void Open() { }
                }
            }

            namespace Mod {
                using Concord;

                [Patch(typeof(Game.Door))]
                public abstract class BaseHooks {
                    [Inject(At.Head, "Open")]
                    protected virtual void OnOpen() { }
                }

                public class Hooks : BaseHooks {
                    protected override void OnOpen() { }
                }
            }
            """;
        Assembly inMetadata = Build(source, true);
        Assembly fromManifest = Build(source, false);

        Assert.Contains("PatchAttribute", CompareEverywhere(inMetadata, fromManifest));
        List<string>[] refusals = new List<string>[2];
        Assembly[] builds = [inMetadata, fromManifest];
        for (int i = 0; i < builds.Length; i++) {
            logs.Clear();
            MethodBase open = builds[i].GetType("Game.Door", true)!.GetMethod("Open")!;
            using (Patcher.Apply(builds[i])) {
                Assert.Equal(["Mod.BaseHooks"], AddedOn(open).Select(injection => injection.Owner));
            }

            refusals[i] = logs.Where(line => line.Contains("Mod.Hooks", StringComparison.Ordinal)).ToList();
            Assert.Contains(refusals[i], line => line.Contains("derives from Mod.BaseHooks", StringComparison.Ordinal));
        }

        Assert.Equal(refusals[0], refusals[1]);
    }

    [Fact]
    public void Apply_OverrideWithAnInjectOfItsOwn_AppliesWithItsOwnArguments() {
        const string source = """
            namespace Game {
                public class Door {
                    public void Open() { }
                }
            }

            namespace Mod {
                using Concord;

                [Patch]
                public abstract class BaseHooks : Game.Door {
                    [Inject(At.Head, "Open")]
                    protected virtual void OnOpen() { }
                }

                public class Hooks : BaseHooks {
                    [Inject(At.Tail, "Open", Priority = 5)]
                    protected override void OnOpen() { }
                }
            }
            """;
        Assembly inMetadata = Build(source, true);
        Assembly fromManifest = Build(source, false);

        Assert.Contains("InjectAttribute", CompareEverywhere(inMetadata, fromManifest));
        foreach (Assembly mod in new[] { inMetadata, fromManifest }) {
            MethodBase open = mod.GetType("Game.Door", true)!.GetMethod("Open")!;
            using (Patcher.Apply(mod)) {
                Injection hooks = Assert.Single(AddedOn(open), injection => injection.Owner == "Mod.Hooks");
                Assert.IsType<InjectAt.Tail>(hooks.At);
                Assert.Equal(5, hooks.Priority);
                Assert.IsType<InjectAt.Head>(Assert.Single(AddedOn(open), injection => injection.Owner == "Mod.BaseHooks").At);
            }
        }
    }

    [Fact]
    public void Apply_DerivedClassWithAPatchDebugOfItsOwn_IsStillADeclaration() {
        const string source = """
            namespace Game {
                public class Door {
                    public void Open() { }
                }
            }

            namespace Mod {
                using Concord;

                [Patch]
                public abstract class BaseHooks : Game.Door {
                    [Inject(At.Head, "Open")]
                    protected virtual void OnOpen() { }
                }

                [PatchDebug]
                public class Hooks : BaseHooks {
                    protected override void OnOpen() { }
                }
            }
            """;
        Assembly inMetadata = Build(source, true);
        Assembly fromManifest = Build(source, false);

        Assert.Contains("PatchDebugAttribute", CompareEverywhere(inMetadata, fromManifest));
        foreach (Assembly mod in new[] { inMetadata, fromManifest }) {
            MethodBase open = mod.GetType("Game.Door", true)!.GetMethod("Open")!;
            using (Patcher.Apply(mod)) {
                Assert.True(Assert.Single(AddedOn(open), injection => injection.Owner == "Mod.Hooks").Debug);
                Assert.False(Assert.Single(AddedOn(open), injection => injection.Owner == "Mod.BaseHooks").Debug);
            }
        }
    }

    [Fact]
    public void Apply_PatchBeforeOnTheBaseClass_DoesNotOrderTheDerivedClass() {
        const string source = """
            namespace Game {
                public class Door {
                    public void Open() { }
                }
            }

            namespace Mod {
                using Concord;

                [Patch]
                [PatchBefore("Other.Mod")]
                public abstract class BaseHooks : Game.Door {
                    [Inject(At.Head, "Open")]
                    protected virtual void OnOpen() { }
                }

                public class Hooks : BaseHooks {
                    protected override void OnOpen() { }
                }
            }
            """;
        Assembly inMetadata = Build(source, true);
        Assembly fromManifest = Build(source, false);

        Assert.Contains("PatchBeforeAttribute", CompareEverywhere(inMetadata, fromManifest));
        foreach (Assembly mod in new[] { inMetadata, fromManifest }) {
            MethodBase open = mod.GetType("Game.Door", true)!.GetMethod("Open")!;
            using (Patcher.Apply(mod)) {
                Assert.Empty(Assert.Single(AddedOn(open), injection => injection.Owner == "Mod.Hooks").BeforeOwners);
                Assert.Equal(["Other.Mod"], Assert.Single(AddedOn(open), injection => injection.Owner == "Mod.BaseHooks").BeforeOwners);
            }
        }
    }

    [Fact]
    public void Apply_ShadowOnTheBaseClassAndOnTheDerivedClass_BothCountForTheDerivedClass() {
        const string source = """
            namespace Game {
                public class Pawn {
                    public int mood;

                    public int Mood { get => mood; set => mood = value; }

                    public void Tick() { }
                }
            }

            namespace Mod {
                using Concord;

                [Patch]
                [Shadow("mood")]
                public abstract partial class BaseShadows : Game.Pawn { }

                [Shadow("Mood")]
                public abstract partial class Shadows : BaseShadows {
                    [Inject(At.Head, "Tick")]
                    private void OnTick() { }
                }
            }
            """;
        Assembly inMetadata = Build(source, true);
        Assembly fromManifest = Build(source, false);

        Assert.Contains("ShadowAttribute", CompareEverywhere(inMetadata, fromManifest));
        foreach (Assembly mod in new[] { inMetadata, fromManifest }) {
            Type shadows = mod.GetType("Mod.Shadows", true)!;
            Assert.Equal(["Mood", "mood"], AttributeStore.GetAll<ShadowAttribute>(shadows).Select(shadow => shadow.Member));
            MethodBase tick = mod.GetType("Game.Pawn", true)!.GetMethod("Tick")!;
            using (Patcher.Apply(mod)) {
                Assert.Equal(["Mod.Shadows"], AddedOn(tick).Select(injection => injection.Owner));
            }
        }
    }

    [Fact]
    public void Apply_ParameterAttributeOnTheBaseMethod_CountsForTheParameterOfTheOverride() {
        const string source = """
            namespace Game {
                public class Door {
                    public void Open() { }
                }
            }

            namespace Mod {
                using Concord;

                [Patch]
                public abstract class BaseHooks : Game.Door {
                    [Inject(At.Head, "Open")]
                    protected virtual void OnOpen([Bound] int seed) { }
                }

                public class Hooks : BaseHooks {
                    protected override void OnOpen(int seed) { }
                }
            }
            """;
        Assembly inMetadata = Build(source, true);
        Assembly fromManifest = Build(source, false);

        Assert.Contains("BoundAttribute", CompareEverywhere(inMetadata, fromManifest));
        foreach (Assembly mod in new[] { inMetadata, fromManifest }) {
            MethodBase open = mod.GetType("Game.Door", true)!.GetMethod("Open")!;
            using (Patcher.Apply(mod)) {
                Injection hooks = Assert.Single(AddedOn(open), injection => injection.Owner == "Mod.Hooks");
                Assert.NotNull(AttributeStore.Get<BoundAttribute>(hooks.InjectionMethod.GetParameters()[0]));
            }
        }
    }

    [Fact]
    public void Apply_PropertyThatOverridesAPropertyWithAnAttribute_GetsTheAttributeOfItsBase() {
        const string source = """
            namespace Game {
                public class Door {
                    public void Open() { }
                }
            }

            namespace Mod {
                using Concord;

                [Patch]
                public abstract class BaseHooks : Game.Door {
                    [InjectInstance]
                    protected abstract Game.Door Self { get; }

                    [Inject(At.Head, "Open")]
                    protected virtual void OnOpen() { }
                }

                public class Hooks : BaseHooks {
                    protected override Game.Door Self => this;

                    protected override void OnOpen() { }
                }
            }
            """;
        Assembly inMetadata = Build(source, true);
        Assembly fromManifest = Build(source, false);

        Assert.Contains("InjectInstanceAttribute", CompareEverywhere(inMetadata, fromManifest));
        foreach (Assembly mod in new[] { inMetadata, fromManifest }) {
            PropertyInfo self = mod.GetType("Mod.Hooks", true)!.GetProperty("Self", Declared)!;
            Assert.NotNull(AttributeStore.Get<InjectInstanceAttribute>(self));
            MethodBase open = mod.GetType("Game.Door", true)!.GetMethod("Open")!;
            using (Patcher.Apply(mod)) {
                Assert.Single(AddedOn(open), injection => injection.Owner == "Mod.Hooks");
            }
        }
    }

    [Fact]
    public void Apply_BaseInAnotherAssemblyWithItsAttributesInMetadata_GivesTheDerivedClassItsPatch() {
        string libraryName = "HooksLibrary_" + Guid.NewGuid().ToString("N");
        const string library = """
            namespace Game {
                public class Door {
                    public void Open() { }
                }
            }

            namespace Library {
                using Concord;

                [Patch]
                public abstract class BaseHooks : Game.Door {
                    [Inject(At.Head, "Open")]
                    protected virtual void OnOpen() { }
                }
            }
            """;
        (Compilation libraryOutput, _) = GeneratorTestHarness.Compile([("Library.cs", library)], true, assemblyName: libraryName);
        byte[] libraryImage = GeneratorTestHarness.Emit(libraryOutput);
        Assembly libraryAssembly = Assembly.Load(libraryImage);
        const string source = """
            namespace Mod {
                using Concord;

                [Patch(typeof(string))]
                public static class Unrelated { }

                public class Hooks : Library.BaseHooks {
                    protected override void OnOpen() { }
                }
            }
            """;
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Compile(
            [("Mod.cs", source)], false, [MetadataReference.CreateFromImage(libraryImage)]);
        Assert.Empty(diagnostics);
        Assembly mod = GeneratorTestHarness.Load(output);
        ResolveEventHandler resolve = (_, request) => new AssemblyName(request.Name).Name == libraryName ? libraryAssembly : null;
        AppDomain.CurrentDomain.AssemblyResolve += resolve;
        try {
            MethodBase open = libraryAssembly.GetType("Game.Door", true)!.GetMethod("Open")!;
            using (Patcher.Apply(mod)) {
                Injection hooks = Assert.Single(AddedOn(open), injection => injection.Owner == "Mod.Hooks");
                Assert.Equal(mod.GetType("Mod.Hooks", true)!.GetMethod("OnOpen", Declared), hooks.InjectionMethod);
            }
        } finally {
            AppDomain.CurrentDomain.AssemblyResolve -= resolve;
        }
    }

    [Fact]
    public void Build_WithTheSymbol_WritesARegistryThatNamesEachPatchClass() {
        Assembly mod = Build(RegistrySample, true);

        PatchRegistryAttribute registry = Assert.Single(mod.GetCustomAttributes<PatchRegistryAttribute>());
        Assert.Equal(RegistryTypeName, registry.RegistryType.FullName);
        Assert.True(PatchDeclarationScanner.TryGetRegistryDeclarations(mod, out IReadOnlyList<Type> declarations));
        Assert.Equal(
            ["Mod.Doors", "Mod.Outer+Hidden", "Mod.Windows"],
            declarations.Select(type => type.FullName!).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void Build_WithoutTheSymbol_WritesNoRegistry() {
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Compile([("Mod.cs", RegistrySample)], false);
        Assert.Empty(diagnostics);
        Assembly mod = GeneratorTestHarness.Load(output);

        Assert.DoesNotContain(output.SyntaxTrees, tree => tree.FilePath.Contains("ConcordPatchRegistry"));
        Assert.Empty(mod.GetCustomAttributes<PatchRegistryAttribute>());
        Assert.Null(mod.GetType(RegistryTypeName));
        Assert.Empty(ConcordAttributesInMetadata(mod));
        Assert.All(mod.GetTypes(), type => Assert.DoesNotContain(type.GetInterfaces(), contract => contract.Namespace == "Concord"));
    }

    [Fact]
    public void Apply_WithTheSymbolAndATypeWhoseBaseIsNotInstalled_TheRegistryStillNamesThePatch() {
        (Compilation absentOutput, _) = GeneratorTestHarness.Compile(
            [("Absent.cs", "namespace Absent { public class Base { } }")], false, assemblyName: "NotInstalled_" + Guid.NewGuid().ToString("N"));
        MetadataReference absent = MetadataReference.CreateFromImage(GeneratorTestHarness.Emit(absentOutput));
        const string source = """
            namespace Game {
                public class Door {
                    public void Open() { }
                }
            }

            namespace Mod {
                using Concord;

                public class Soft : Absent.Base { }

                [Patch(typeof(Game.Door))]
                public static class Doors {
                    [Inject(At.Head, "Open")]
                    private static void OnOpen() { }
                }
            }
            """;
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Compile([("Mod.cs", source)], true, [absent]);
        Assert.Empty(diagnostics);
        Assembly mod = GeneratorTestHarness.Load(output);
        MethodBase open = mod.GetType("Game.Door", true)!.GetMethod("Open")!;

        Assert.Throws<ReflectionTypeLoadException>(() => mod.GetTypes());
        Assert.True(PatchDeclarationScanner.TryGetRegistryDeclarations(mod, out IReadOnlyList<Type> declarations));
        Assert.Equal(["Mod.Doors"], declarations.Select(type => type.FullName!));
        using (Patcher.Apply(mod)) {
            Assert.Equal(["Mod.Doors"], backend.OwnersOf(open));
        }
    }

    [Fact]
    public void Apply_WithAManifestAndARegistry_AppliesEachDeclarationOnce() {
        Assembly mod = Build(RegistrySample, true);
        Assert.NotNull(mod.GetType(ManifestTypeName));
        Assert.NotNull(mod.GetType(RegistryTypeName));
        MethodBase open = mod.GetType("Game.Door", true)!.GetMethod("Open")!;
        MethodBase close = mod.GetType("Game.Window", true)!.GetMethod("Close")!;

        using (Patcher.Apply(mod)) {
            Assert.Equal(["Mod.Doors", "Mod.Outer+Hidden"], AddedOn(open).Select(injection => injection.Owner).OrderBy(owner => owner, StringComparer.Ordinal));
            Assert.Equal(["Mod.Windows"], AddedOn(close).Select(injection => injection.Owner));
        }
    }

    [Fact]
    public void Build_WithTheSymbolAndAHandWrittenRegistry_KeepsTheHandWrittenOne() {
        Assembly mod = Build(KitchenSink, true);

        PatchRegistryAttribute registry = Assert.Single(mod.GetCustomAttributes<PatchRegistryAttribute>());
        Assert.Equal("Mod.Registry", registry.RegistryType.FullName);
        Assert.Null(mod.GetType(RegistryTypeName));
    }

    private static Assembly Build(string source, bool attributesInMetadata) {
        return Build([("Mod.cs", source)], attributesInMetadata);
    }

    private static Assembly Build(IReadOnlyList<(string Path, string Source)> sources, bool attributesInMetadata) {
        (Compilation output, ImmutableArray<Diagnostic> diagnostics) = GeneratorTestHarness.Compile(sources, attributesInMetadata);
        GeneratorTestHarness.AssertCompiles(output);
        Assert.Empty(diagnostics);
        return GeneratorTestHarness.Load(output);
    }

    private static object? ConstantOf(Type hooks, string method) {
        InjectAttribute inject = AttributeStore.Get<InjectAttribute>(hooks.GetMethod(method, Declared)!)!;
        return typeof(InjectAttribute).GetField("constant", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(inject);
    }

    private IReadOnlyList<Injection> AddedOn(MethodBase target) {
        return backend.ComposedDetours.Where(entry => entry.Target.MethodHandle.Equals(target.MethodHandle)).SelectMany(entry => entry.Added).ToList();
    }

    private MethodBase OnlyInjection(Type target, string name) {
        return Assert.Single(AddedOn(target.GetMethod(name)!)).InjectionMethod;
    }

    private static List<string> ConcordAttributesInMetadata(Assembly assembly) {
        List<string> found = [];
        AddConcord(found, "assembly", assembly.GetCustomAttributesData());
        foreach (Type type in assembly.GetTypes()) {
            AddConcord(found, type.FullName!, type.GetCustomAttributesData());
            foreach (MemberInfo member in type.GetMembers(Declared)) {
                AddConcord(found, type.FullName + "." + member, member.GetCustomAttributesData());
                if (member is MethodBase method) {
                    foreach (ParameterInfo parameter in method.GetParameters()) {
                        AddConcord(found, type.FullName + "." + member + " #" + parameter.Position, parameter.GetCustomAttributesData());
                    }
                }
            }
        }

        return found;
    }

    private static void AddConcord(List<string> found, string where, IList<CustomAttributeData> attributes) {
        foreach (CustomAttributeData attribute in attributes) {
            if (attribute.AttributeType.Namespace == "Concord") {
                found.Add(where + ": " + attribute.AttributeType.Name);
            }
        }
    }

    private static HashSet<string> CompareEverywhere(Assembly inMetadata, Assembly fromManifest) {
        HashSet<string> seen = [];
        List<Attribute> assembly = Concord(inMetadata.GetCustomAttributes(false))
            .Where(attribute => attribute is not PatchRegistryAttribute { RegistryType.FullName: RegistryTypeName })
            .ToList();
        Compare(seen, "assembly", assembly, One(AttributeStore.Get<PatchRegistryAttribute>(fromManifest)));
        foreach (Type type in inMetadata.GetTypes()) {
            if (type.FullName is ManifestTypeName or RegistryTypeName) {
                continue;
            }

            Type twin = fromManifest.GetType(type.FullName!, true)!;
            Compare(seen, type.FullName!, Concord(type.GetCustomAttributes(true)), Concord(AttributeStore.GetAll<Attribute>(twin)));
            foreach (MemberInfo member in type.GetMembers(Declared)) {
                MemberInfo other = Assert.Single(twin.GetMembers(Declared), candidate => candidate.MemberType == member.MemberType && candidate.ToString() == member.ToString());
                string where = type.FullName + "." + member;
                Compare(seen, where, Concord(Attribute.GetCustomAttributes(member, true)), Concord(AttributeStore.GetAll<Attribute>(other)));
                if (member is MethodBase method) {
                    ParameterInfo[] parameters = ((MethodBase)other).GetParameters();
                    foreach (ParameterInfo parameter in method.GetParameters()) {
                        Compare(
                            seen,
                            where + " #" + parameter.Position,
                            Concord(Attribute.GetCustomAttributes(parameter, true)),
                            One(AttributeStore.Get<Attribute>(parameters[parameter.Position])));
                    }
                }
            }
        }

        return seen;
    }

    private static void Compare(HashSet<string> seen, string where, List<Attribute> expected, List<Attribute> actual) {
        Assert.True(
            expected.Select(Describe).SequenceEqual(actual.Select(Describe)),
            where + ": expected [" + string.Join("; ", expected.Select(Describe)) + "] but the manifest gave [" + string.Join("; ", actual.Select(Describe)) + "]");
        foreach (Attribute attribute in expected) {
            seen.Add(attribute.GetType().Name);
        }
    }

    private static List<Attribute> Concord(IEnumerable<object> attributes) {
        return attributes.OfType<Attribute>().Where(attribute => attribute.GetType().Namespace == "Concord").ToList();
    }

    private static List<Attribute> One(Attribute? attribute) {
        return Concord(attribute is null ? [] : [attribute]);
    }

    private static string Describe(Attribute attribute) {
        List<string> fields = [];
        for (Type? type = attribute.GetType(); type is not null && type != typeof(Attribute); type = type.BaseType) {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
                fields.Add(field.Name + "=" + Render(field.GetValue(attribute)));
            }
        }

        return attribute.GetType().Name + "(" + string.Join(", ", fields) + ")";
    }

    private sealed class AttachedCalls : IAttachedPropertyRegistry {
        public List<(Type Declaration, string Name, Type Value)> Calls { get; } = [];

        public void RegisterAttachedProperty(Type declarationType, Type baseType, string name, Type valueType, IAttachedSlot slot) {
            Calls.Add((declarationType, name, valueType));
        }
    }

    private static string Render(object? value) {
        return value switch {
            null => "null",
            Type type => "typeof(" + type + ")",
            string text => "\"" + text + "\"",
            IEnumerable items => "[" + string.Join(", ", items.Cast<object?>().Select(Render)) + "]",
            float number => "float:" + number.ToString("R", CultureInfo.InvariantCulture),
            double number => "double:" + number.ToString("R", CultureInfo.InvariantCulture),
            Enum member => member.GetType().FullName + "." + member,
            _ => value.GetType().Name + ":" + Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }
}
