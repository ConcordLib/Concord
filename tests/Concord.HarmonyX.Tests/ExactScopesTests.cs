using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;
using Concord.Emit;
using Concord.MonoMod;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using Xunit;
using Xunit.Abstractions;
using Sre = System.Reflection.Emit;

namespace Concord.HarmonyX.Tests;

public static class TwinCalls {
    public static readonly List<string> Ran = [];

    public static readonly List<Assembly> Trail = [];

    public static Assembly? Reached;

    public static Assembly? Element;

    public static void Reach(Assembly copy) {
        Reached = copy;
        Trail.Add(copy);
    }

    public static void OnHead() {
        Ran.Add("concord");
    }

    public static void Prefix() {
        Ran.Add("prefix");
    }
}

public static class ChainTargets {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int TwoPrefixes() {
        return 11;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int PlantedPrefix() {
        return 13;
    }
}

public sealed class TwinHashCollisionFactAttribute : FactAttribute {
    internal const int Cap = 64;

    internal static readonly (MethodInfo First, MethodInfo Second, int Load)? Found = Probe();

    public TwinHashCollisionFactAttribute() {
        if (Found is null) {
            Skip = $"mono's identity hash did not collide in {Cap} loads of one twin image, so no two live copies share a scope stamp";
        }
    }

    private static (MethodInfo First, MethodInfo Second, int Load)? Probe() {
        byte[] image = ExactScopesTests.TwinImage();
        Dictionary<int, MethodInfo> byHash = new Dictionary<int, MethodInfo>();
        for (int load = 1; load <= Cap; load++) {
            MethodInfo probe = ExactScopesTests.LoadOne(image);
            int hash = probe.DeclaringType!.Assembly.GetHashCode();
            if (byHash.TryGetValue(hash, out MethodInfo? earlier)) {
                return (earlier, probe, load);
            }

            byHash[hash] = probe;
            GC.Collect();
        }

        return null;
    }
}

public sealed class ExactScopesTests : IDisposable {
    private const int Constant = 7;

    private readonly ITestOutputHelper output;

    private readonly List<string> lines = [];

    private readonly Action<string> logged = HarmonyXBackend.Log;

    private readonly List<Harmony> patchers = [];

    private readonly List<IBackendHandle> handles = [];

    public ExactScopesTests(ITestOutputHelper output) {
        this.output = output;
        HarmonyXBackend.Log = line => lines.Add(line);
        HarmonyXEntryPoint.Install(line => lines.Add(line), null);
        TwinCalls.Ran.Clear();
        TwinCalls.Trail.Clear();
        TwinCalls.Reached = null;
        TwinCalls.Element = null;
    }

    public void Dispose() {
        foreach (IBackendHandle handle in handles) {
            handle.Dispose();
        }

        foreach (Harmony patcher in patchers) {
            patcher.UnpatchSelf();
        }

        HarmonyXBackend.Log = logged;
        TwinCalls.Ran.Clear();
    }

    [Fact]
    public void ThePass_FindsTheImporterAndTheCacheOnMonoMod22() {
        Assert.Equal("ReflectionImporter", ExactScopes.ImporterProperty);
        Assert.Equal("CachedAsms", ExactScopes.StampedAssembliesField);
        Assert.Equal("MonoMod.Utils.ReflectionHelper", ExactScopes.HelperType);
        Assert.Equal("AssemblyCache", ExactScopes.AssemblyCacheField);

        (MethodInfo _, MethodInfo own) = LoadTwins();
        using DynamicMethodDefinition definition = new DynamicMethodDefinition(own);
        ModuleDefinition module = definition.Definition.Module;

        int bound = ExactScopes.Bind(module);
        output.WriteLine($"bound {bound} on the module of a byte-loaded copy");
        Assert.True(bound > 0);
    }

    [Fact]
    public void AScopeKeyPlantedOnAnotherCopy_TheInjectedBodyStillCallsIntoItsOwnCopy() {
        (MethodInfo other, MethodInfo own) = LoadTwins();
        Assembly ownCopy = own.DeclaringType!.Assembly;
        Assembly otherCopy = other.DeclaringType!.Assembly;

        PlantScope(ownCopy, otherCopy);
        Assert.Same(otherCopy, BoundScope(ownCopy));

        IBackendHandle concord = Inject(own);

        Assert.True(concord.IsApplied, Lines());
        Assert.Equal(Constant, own.Invoke(null, null));
        Assert.Contains("concord", TwinCalls.Ran);
        Assert.Same(ownCopy, TwinCalls.Reached);
        Assert.Same(ownCopy, BoundScope(ownCopy));
    }

    [Fact]
    public void AHarmonyXPrefixFirstThenAConcordInjection_BothRunOnTheByteLoadedCopy() {
        (MethodInfo _, MethodInfo own) = LoadTwins();

        Prefix(own);
        IBackendHandle concord = Inject(own);

        AssertBothRan(own, concord);
    }

    [Fact]
    public void AConcordInjectionFirstThenAHarmonyXPrefix_BothRunOnTheByteLoadedCopy() {
        (MethodInfo _, MethodInfo own) = LoadTwins();

        IBackendHandle concord = Inject(own);
        Prefix(own);

        AssertBothRan(own, concord);
    }

    [TwinHashCollisionFact]
    public void AScratchBodyCallingBothCollidingCopies_OnMonoMod22_EachCallRunsInItsOwnCopy() {
        (MethodInfo first, MethodInfo second, int load) = TwinHashCollisionFactAttribute.Found!.Value;
        output.WriteLine($"collided at load {load} of {TwinHashCollisionFactAttribute.Cap}");
        Assert.Equal(first.DeclaringType!.Assembly.GetHashCode(), second.DeclaringType!.Assembly.GetHashCode());

        using IScratchBody body = new MonoModScratchGenerator().Begin("ExactScopesBoth", typeof(void), Type.EmptyTypes, second);
        body.Emit(Sre.OpCodes.Call, MarkOf(first));
        body.Emit(Sre.OpCodes.Call, MarkOf(second));
        body.Emit(Sre.OpCodes.Ret);
        MethodInfo both = body.Finish();

        TwinCalls.Trail.Clear();
        both.Invoke(null, null);

        Assert.Equal(new[] { first.DeclaringType.Assembly, second.DeclaringType.Assembly }, TwinCalls.Trail);
    }

    [Fact]
    public void TwoCopiesLoadedBeforeTheFirstScratchBody_OnMonoMod22_EachBodyEnumeratesItsOwnCopy() {
        (MethodInfo first, MethodInfo second) = LoadTwins();

        ScratchEnumerating(first).Invoke(null, null);
        Assert.Same(first.DeclaringType!.Assembly, TwinCalls.Element);

        ScratchEnumerating(second).Invoke(null, null);
        Assert.Same(second.DeclaringType!.Assembly, TwinCalls.Element);
    }

    [Fact]
    public void TwoCopiesLoadedBeforeTheFirstInjection_EachChainBodyEnumeratesItsOwnCopy() {
        (MethodInfo first, MethodInfo second) = LoadTwins();

        Assert.True(Inject(first).IsApplied, Lines());
        AssertEnumeratesItsOwnCopy(first);

        Assert.True(Inject(second).IsApplied, Lines());
        AssertEnumeratesItsOwnCopy(second);
        AssertEnumeratesItsOwnCopy(first);
    }

    [TwinHashCollisionFact]
    public void TheChainModule_AfterHarmonyXImportedTwoCollidingCopies_HoldsOneReferencePerCopy() {
        (MethodInfo first, MethodInfo second, int _) = TwinHashCollisionFactAttribute.Found!.Value;
        Assembly firstCopy = first.DeclaringType!.Assembly;
        Assembly secondCopy = second.DeclaringType!.Assembly;
        MethodInfo target = typeof(ChainTargets).GetMethod(nameof(ChainTargets.TwoPrefixes))!;
        object? firstScope = null;
        object? secondScope = null;

        Inject(target);
        ILHookConfig last = new ILHookConfig { ID = "exact-scopes.spy", After = new[] { "*" } };
        using ILHook spy = new ILHook(target, il => {
            IDictionary stamped = StampedOf(il.Method.Module);
            firstScope = stamped[firstCopy];
            secondScope = stamped[secondCopy];
        }, ref last);

        Prefix(target, MarkOf(first));
        Prefix(target, MarkOf(second));

        TwinCalls.Trail.Clear();
        Assert.Equal(11, target.Invoke(null, null));

        output.WriteLine($"first copy's reference: {firstScope}; second copy's reference: {secondScope}");
        Assert.NotNull(firstScope);
        Assert.NotNull(secondScope);
        Assert.NotSame(firstScope, secondScope);
        Assert.Contains(firstCopy, TwinCalls.Trail);
        Assert.Contains(secondCopy, TwinCalls.Trail);
        Assert.Contains("concord", TwinCalls.Ran);
    }

    [Fact]
    public void AHarmonyXPrefixDefinedInAByteLoadedCopy_PastAScopePlantedOnAnotherCopy_RunsInItsOwnCopy() {
        (MethodInfo other, MethodInfo own) = LoadTwins();
        Assembly ownCopy = own.DeclaringType!.Assembly;
        MethodInfo target = typeof(ChainTargets).GetMethod(nameof(ChainTargets.PlantedPrefix))!;

        Assert.True(Inject(target).IsApplied, Lines());
        PlantScope(ownCopy, other.DeclaringType!.Assembly);
        Prefix(target, MarkOf(own));

        TwinCalls.Reached = null;
        Assert.Equal(13, target.Invoke(null, null));

        Assert.Same(ownCopy, TwinCalls.Reached);
        Assert.Contains("concord", TwinCalls.Ran);
        Assert.Same(ownCopy, BoundScope(ownCopy));
    }

    private static void AssertEnumeratesItsOwnCopy(MethodInfo probe) {
        TwinCalls.Reached = null;
        TwinCalls.Element = null;

        Assert.Equal(Constant, probe.Invoke(null, null));

        Assert.Same(probe.DeclaringType!.Assembly, TwinCalls.Reached);
        Assert.Same(probe.DeclaringType.Assembly, TwinCalls.Element);
    }

    private static (MethodInfo Other, MethodInfo Own) LoadTwins() {
        byte[] image = TwinImage();
        MethodInfo other = LoadOne(image);
        MethodInfo own = LoadOne(image);
        for (int attempt = 0; attempt < 5 && own.DeclaringType!.Assembly.GetHashCode() == other.DeclaringType!.Assembly.GetHashCode(); attempt++) {
            own = LoadOne(image);
        }

        Assert.NotSame(other.DeclaringType, own.DeclaringType);
        Assert.Equal(other.DeclaringType!.Assembly.FullName, own.DeclaringType!.Assembly.FullName);
        Assert.NotEqual(other.DeclaringType.Assembly.GetHashCode(), own.DeclaringType.Assembly.GetHashCode());
        return (other, own);
    }

    internal static MethodInfo LoadOne(byte[] image) {
        return Assembly.Load(AssemblyImage.WithFreshModuleId(image)).GetType("TwinHolder")!.GetMethod("Probe")!;
    }

    internal static byte[] TwinImage() {
        string name = "HxTwin" + Guid.NewGuid().ToString("N").Substring(0, 8);
        using AssemblyDefinition assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition(name, new Version(1, 0, 0, 0)), name + ".dll", ModuleKind.Dll);
        ModuleDefinition module = assembly.MainModule;

        TypeDefinition holder = new TypeDefinition(
            string.Empty,
            "TwinHolder",
            Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
            module.TypeSystem.Object);
        module.Types.Add(holder);

        MethodDefinition mark = new MethodDefinition(
            "Mark", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.HideBySig, module.TypeSystem.Void);
        mark.ImplAttributes = Mono.Cecil.MethodImplAttributes.NoInlining;
        holder.Methods.Add(mark);
        ILProcessor marking = mark.Body.GetILProcessor();
        marking.Emit(OpCodes.Ldtoken, holder);
        marking.Emit(OpCodes.Call, module.ImportReference(typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))));
        marking.Emit(OpCodes.Callvirt, module.ImportReference(typeof(Type).GetProperty(nameof(Type.Assembly))!.GetGetMethod()));
        marking.Emit(OpCodes.Call, module.ImportReference(typeof(TwinCalls).GetMethod(nameof(TwinCalls.Reach))));
        marking.Emit(OpCodes.Ret);

        TypeDefinition twin = new TypeDefinition(
            string.Empty, "TwinType", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(twin);
        MethodDefinition made = new MethodDefinition(
            ".ctor",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        twin.Methods.Add(made);
        ILProcessor making = made.Body.GetILProcessor();
        making.Emit(OpCodes.Ldarg_0);
        making.Emit(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)));
        making.Emit(OpCodes.Ret);

        GenericInstanceType list = new GenericInstanceType(module.ImportReference(typeof(List<>)));
        list.GenericArguments.Add(twin);
        GenericInstanceType enumerator = new GenericInstanceType(module.ImportReference(typeof(List<>.Enumerator)));
        enumerator.GenericArguments.Add(twin);

        MethodDefinition probe = new MethodDefinition(
            "Probe", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static | Mono.Cecil.MethodAttributes.HideBySig, module.TypeSystem.Int32);
        probe.ImplAttributes = Mono.Cecil.MethodImplAttributes.NoInlining;
        holder.Methods.Add(probe);
        VariableDefinition walker = new VariableDefinition(enumerator);
        probe.Body.Variables.Add(walker);
        probe.Body.InitLocals = true;
        ILProcessor probing = probe.Body.GetILProcessor();
        probing.Emit(OpCodes.Call, mark);
        probing.Emit(OpCodes.Newobj, Hosted(module, typeof(List<>).GetConstructor(Type.EmptyTypes)!, list));
        probing.Emit(OpCodes.Dup);
        probing.Emit(OpCodes.Newobj, made);
        probing.Emit(OpCodes.Callvirt, Hosted(module, typeof(List<>).GetMethod("Add")!, list));
        probing.Emit(OpCodes.Callvirt, Hosted(module, typeof(List<>).GetMethod("GetEnumerator")!, list));
        probing.Emit(OpCodes.Stloc, walker);
        probing.Emit(OpCodes.Ldloca, walker);
        probing.Emit(OpCodes.Call, Hosted(module, typeof(List<>.Enumerator).GetMethod("MoveNext")!, enumerator));
        probing.Emit(OpCodes.Pop);
        probing.Emit(OpCodes.Ldloca, walker);
        probing.Emit(OpCodes.Call, Hosted(module, typeof(List<>.Enumerator).GetProperty("Current")!.GetGetMethod()!, enumerator));
        probing.Emit(OpCodes.Callvirt, module.ImportReference(typeof(object).GetMethod(nameof(GetType))));
        probing.Emit(OpCodes.Callvirt, module.ImportReference(typeof(Type).GetProperty(nameof(Type.Assembly))!.GetGetMethod()));
        probing.Emit(OpCodes.Stsfld, module.ImportReference(typeof(TwinCalls).GetField(nameof(TwinCalls.Element))));
        probing.Emit(OpCodes.Ldc_I4, Constant);
        probing.Emit(OpCodes.Ret);

        using MemoryStream written = new MemoryStream();
        assembly.Write(written);
        return written.ToArray();
    }

    private static MethodReference Hosted(ModuleDefinition module, MethodBase open, TypeReference host) {
        MethodReference definition = module.ImportReference(open);
        MethodReference hosted = new MethodReference(definition.Name, definition.ReturnType, host) {
            HasThis = definition.HasThis,
            ExplicitThis = definition.ExplicitThis,
            CallingConvention = definition.CallingConvention,
        };

        foreach (ParameterDefinition parameter in definition.Parameters) {
            hosted.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
        }

        return hosted;
    }

    private static MethodInfo MarkOf(MethodInfo probe) {
        return probe.DeclaringType!.GetMethod("Mark")!;
    }

    private static Type TwinOf(MethodInfo probe) {
        return probe.DeclaringType!.Assembly.GetType("TwinType")!;
    }

    private static MethodInfo ScratchEnumerating(MethodInfo probe) {
        Type twin = TwinOf(probe);
        Type list = typeof(List<>).MakeGenericType(twin);
        Type enumerator = typeof(List<>.Enumerator).MakeGenericType(twin);

        using IScratchBody body = new MonoModScratchGenerator().Begin("ExactScopesEnumerating", typeof(void), Type.EmptyTypes, probe);
        int walker = body.DeclareLocal(enumerator, false);
        body.Emit(Sre.OpCodes.Newobj, list.GetConstructor(Type.EmptyTypes)!);
        body.Emit(Sre.OpCodes.Dup);
        body.Emit(Sre.OpCodes.Newobj, twin.GetConstructor(Type.EmptyTypes)!);
        body.Emit(Sre.OpCodes.Callvirt, list.GetMethod("Add")!);
        body.Emit(Sre.OpCodes.Callvirt, list.GetMethod("GetEnumerator")!);
        body.Emit(Sre.OpCodes.Stloc, walker);
        body.Emit(Sre.OpCodes.Ldloca, walker);
        body.Emit(Sre.OpCodes.Call, enumerator.GetMethod("MoveNext")!);
        body.Emit(Sre.OpCodes.Pop);
        body.Emit(Sre.OpCodes.Ldloca, walker);
        body.Emit(Sre.OpCodes.Call, enumerator.GetProperty("Current")!.GetGetMethod()!);
        body.Emit(Sre.OpCodes.Callvirt, typeof(object).GetMethod(nameof(GetType))!);
        body.Emit(Sre.OpCodes.Callvirt, typeof(Type).GetProperty(nameof(Type.Assembly))!.GetGetMethod()!);
        body.Emit(Sre.OpCodes.Stsfld, typeof(TwinCalls).GetField(nameof(TwinCalls.Element))!);
        body.Emit(Sre.OpCodes.Ret);
        return body.Finish();
    }

    private static IDictionary StampedOf(ModuleDefinition module) {
        object importer = typeof(ModuleDefinition).GetProperty(ExactScopes.ImporterProperty, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(module, null)!;
        return (IDictionary)importer.GetType().GetField(ExactScopes.StampedAssembliesField, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(importer)!;
    }

    private static void PlantScope(Assembly key, Assembly value) {
        IDictionary cache = AssemblyCache();
        lock (cache) {
            cache[key.GetRuntimeHashedFullName()] = new WeakReference(value);
        }
    }

    private static Assembly? BoundScope(Assembly key) {
        IDictionary cache = AssemblyCache();
        lock (cache) {
            return (cache[key.GetRuntimeHashedFullName()] as WeakReference)?.Target as Assembly;
        }
    }

    private static IDictionary AssemblyCache() {
        return typeof(ReflectionHelper).GetField(ExactScopes.AssemblyCacheField, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as IDictionary
               ?? throw new InvalidOperationException("MonoMod 22's ReflectionHelper carries no AssemblyCache dictionary");
    }

    private void AssertBothRan(MethodInfo own, IBackendHandle concord) {
        Assert.True(concord.IsApplied, Lines());
        TwinCalls.Ran.Clear();
        TwinCalls.Reached = null;

        Assert.Equal(Constant, own.Invoke(null, null));
        Assert.Contains("prefix", TwinCalls.Ran);
        Assert.Contains("concord", TwinCalls.Ran);
        Assert.Same(own.DeclaringType!.Assembly, TwinCalls.Reached);
        Assert.Contains(HarmonyXBackend.ForeignOwner, PatchBackend.Current.OwnersOf(own));
        Assert.Contains("exact-scopes.concord", PatchBackend.Current.OwnersOf(own));
    }

    private IBackendHandle Inject(MethodBase target) {
        IBackendHandle handle = PatchBackend.Current.ApplyComposed(
            target,
            new[] { new Injection(typeof(TwinCalls).GetMethod(nameof(TwinCalls.OnHead))!, new InjectAt.Head(), "exact-scopes.concord", 0) });

        handles.Add(handle);
        return handle;
    }

    private void Prefix(MethodBase target) {
        Prefix(target, typeof(TwinCalls).GetMethod(nameof(TwinCalls.Prefix))!);
    }

    private void Prefix(MethodBase target, MethodInfo prefix) {
        Harmony patcher = new Harmony("exact-scopes.prefix." + Guid.NewGuid().ToString("N"));
        patchers.Add(patcher);
        patcher.Patch(target, new HarmonyMethod(prefix));
    }

    private string Lines() {
        return lines.Count == 0 ? "Nothing was logged." : "Logged: " + string.Join(" | ", lines);
    }
}
