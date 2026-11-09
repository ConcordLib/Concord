using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Concord;
using Verse;
using Waylith.Injection;

namespace Concord.RimWorld.StaleTest;

public class StaleWrapperMod : Mod {
    private const int FrameCap = 600;

    private static readonly MethodInfo GetBaseCount =
        typeof(IngredientCount).GetMethod(nameof(IngredientCount.GetBaseCount), BindingFlags.Public | BindingFlags.Instance);

    private static MethodInfo Accessor;

    public StaleWrapperMod(ModContentPack content) : base(content) {
        Patcher.Apply(typeof(StaleWrapperMod).Assembly);
        Log.Message("[STALE] applied");
        LongEventHandler.ExecuteWhenFinished(() => LongEventHandler.ExecuteWhenFinished(Run));
    }

    private static void Run() {
        try {
            ForwardRuntimeLog();
            HookChainFront();
            Injection.RegisterStaleMethodHandler(Offered);
            Log.Message("[STALE] injection-supported=" + Injection.IsSupported);

            Reading before = Call();
            Log.Message("[STALE] before=" + before);
            if (before.Counted != 1 || before.Thrown != null) {
                Fail("the patch is not running before the injection, wanted counted=1 and no throw");
                return;
            }

            Accessor = BuildAccessorAfterTheWrapperExists();
            Log.Message("[STALE] accessor loaded " + Name(Accessor) + " rewritten=" + Injection.HasRewrittenBody(Accessor));
            Injection.ProcessAssemblies(new[] { Accessor.Module.Assembly });
            if (Injection.HasRewrittenBody(Accessor)) {
                Log.Message("[STALE] field applied synchronously");
                Measure();
                return;
            }

            Current.Root.StartCoroutine(WaitThenMeasure());
        } catch (Exception e) {
            Fail(e.ToString());
        }
    }

    private static IEnumerator WaitThenMeasure() {
        int frames = 0;
        while (!Injection.HasRewrittenBody(Accessor) && frames < FrameCap) {
            frames++;
            yield return null;
        }

        if (!Injection.HasRewrittenBody(Accessor)) {
            Fail("the injected field never applied within " + FrameCap + " frames");
            yield break;
        }

        Log.Message("[STALE] field applied after frames=" + frames);
        Measure();
    }

    private static void Measure() {
        try {
            Reading after = Call();
            Log.Message("[STALE] after=" + after);
            if (after.Counted != 1 || after.Thrown != null) {
                Fail("the patch did not survive the injection, wanted exactly one count on this call and no throw");
                return;
            }

            Log.Message("[STALE] done");
        } catch (Exception e) {
            Fail(e.ToString());
        }
    }

    private static void Fail(string reason) {
        Log.Message("[STALE] FAILED " + reason);
        Log.Message("[STALE] done");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Reading Call() {
        int start = Probe.Counted;
        try {
            object value = GetBaseCount.Invoke(NewSubject(), null);
            return new Reading(Probe.Counted - start, value.ToString(), null);
        } catch (TargetInvocationException e) {
            Exception inner = e.InnerException ?? e;
            return new Reading(Probe.Counted - start, "-", inner.GetType().Name + ": " + inner.Message);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object NewSubject() {
        return new IngredientCount();
    }

    private static MethodInfo BuildAccessorAfterTheWrapperExists() {
        AssemblyName name = new AssemblyName("StaleWrapperProbe.Injected");
        PersistedAssemblyBuilder assembly = new PersistedAssemblyBuilder(name, typeof(object).Assembly);
        ModuleBuilder module = assembly.DefineDynamicModule(name.Name);

        TypeBuilder accessors = module.DefineType(
            "StaleWrapperFields",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class);

        MethodBuilder accessor = accessors.DefineMethod(
            "Marker",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int),
            new[] { typeof(IngredientCount) });
        accessor.SetCustomAttribute(
            new CustomAttributeBuilder(typeof(InjectedFieldAttribute).GetConstructor(Type.EmptyTypes), Array.Empty<object>()));

        ILGenerator il = accessor.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        accessors.CreateType();

        using MemoryStream stream = new MemoryStream();
        assembly.Save(stream);
        Type built = Assembly.Load(stream.ToArray()).GetType("StaleWrapperFields");
        return built.GetMethod("Marker", BindingFlags.Public | BindingFlags.Static);
    }

    private static void HookChainFront() {
        Injection.HarmonyRegenerateDelegate previous = Injection.HarmonyRegenerate;
        Log.Message("[STALE] chain-front-was-set=" + (previous != null));
        Injection.HarmonyRegenerate = method => {
            Log.Message("[STALE] reported " + Name(method));
            return previous != null && previous(method);
        };
    }

    private static bool Offered(MethodBase method) {
        Log.Message("[STALE] offered " + Name(method));
        return false;
    }

    private static string Name(MethodBase method) {
        return (method.DeclaringType?.FullName ?? "<dynamic>") + "." + method.Name;
    }

    private static void ForwardRuntimeLog() {
        Action<InjectionLogLevel, string> previous = Injection.LogSink;
        Injection.LogSink = (level, text) => {
            previous?.Invoke(level, text);
            Log.Message("[STALE] runtime " + level + " " + text);
        };
    }

    private readonly struct Reading {
        public Reading(int counted, string value, string thrown) {
            Counted = counted;
            Value = value;
            Thrown = thrown;
        }

        public int Counted { get; }

        public string Value { get; }

        public string Thrown { get; }

        public override string ToString() {
            return "counted=" + Counted + " value=" + Value + " thrown=" + (Thrown ?? "none");
        }
    }
}
