using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Waylith.Tests;

namespace Concord.Waylith.ForkTests;

public enum ForkGap {
    None,
    Runtime,
}

public static class ForkRuntime {
    public const string GapMarker = "fork-gap:";

    private const int RawPriority = 1000;

    private static readonly ConcurrentDictionary<MethodBase, List<IPatchHandle>> Applied = new();

    static ForkRuntime() {
        HostRead = Register();
    }

    public static bool HostRead { get; }

    public static ForkGap Gap => RuntimeCapability.HasPipeline ? ForkGap.None : ForkGap.Runtime;

    public static bool Unavailable => Gap != ForkGap.None;

    public static string Explain =>
        RuntimeCapability.IsAvailable && !RuntimeCapability.HasPipeline
            ? $"'{RuntimeCapability.UpdaterTypeName}' replaces bodies on '{RuntimeCapability.Runtime}' but " +
                $"'{RuntimeCapability.TransformerTypeName}' is not here, so nothing can stage a composed body " +
                "and every fact below the pipeline stands down"
            : RuntimeCapability.Explain();

    public static byte[] Returning(int value) {
        return Returning(value, 1);
    }

    public static byte[] Returning(int value, int maxStack) {
        ComposedBody body = new ComposedBody(
            [
                new ComposedInstruction(new CodeInstruction(OpCodes.Ldc_I4, value), null, null),
                new ComposedInstruction(new CodeInstruction(OpCodes.Ret), null, null),
            ],
            [],
            [],
            false);

        return BodyEncoder.Encode(body, new NoTokens(), new FixedStack(maxStack)).Bytes;
    }

    public static byte[] Summing(int terms, int maxStack) {
        List<ComposedInstruction> stream = [];
        for (int i = 0; i < terms; i++) {
            stream.Add(new ComposedInstruction(new CodeInstruction(OpCodes.Ldc_I4_1), null, null));
        }

        for (int i = 1; i < terms; i++) {
            stream.Add(new ComposedInstruction(new CodeInstruction(OpCodes.Add), null, null));
        }

        stream.Add(new ComposedInstruction(new CodeInstruction(OpCodes.Ret), null, null));
        ComposedBody body = new ComposedBody(stream, [], [], false);
        return BodyEncoder.Encode(body, new NoTokens(), new FixedStack(maxStack)).Bytes;
    }

    public static void Write(MethodBase target, byte[] body, IReadOnlyList<OffsetPair> offsetMap) {
        RawStage.Install(target, body, offsetMap);
    }

    public static void Write(MethodBase target, int value) {
        Write(target, Returning(value), []);
    }

    public static void Write(MethodBase target, int value, int maxStack) {
        Write(target, Returning(value, maxStack), []);
    }

    public static void WriteSum(MethodBase target, int terms, int maxStack) {
        Write(target, Summing(terms, maxStack), []);
    }

    public static IPatchHandle Apply(MethodBase target, MethodBase injection, At at) {
        IPatchHandle handle = Patcher.Patch(target, injection, at);
        Applied.GetOrAdd(target, static _ => []).Add(handle);
        return handle;
    }

    public static IPatchHandle Apply(MethodBase target, Type injections, string injection, At at) {
        return Apply(target, injections.GetMethod(injection)!, at);
    }

    public static IPatchHandle Track(MethodBase target, IPatchHandle handle) {
        Applied.GetOrAdd(target, static _ => []).Add(handle);
        return handle;
    }

    public static void Restore(MethodBase target) {
        if (Applied.TryRemove(target, out List<IPatchHandle>? handles)) {
            for (int i = handles.Count - 1; i >= 0; i--) {
                handles[i].Dispose();
            }
        }

        RawStage.Remove(target);
    }

    public static bool IsDefaultBody(MethodBase target) {
        return RawStage.IsDefaultBody(target);
    }

    private static bool Register() {
        WrapperComposer.CaptureComposedBody = true;
        if (RuntimeCapability.HasPipeline) {
            AdapterEntryPoint.Install(null, []);
        }

        return AdapterEntryPoint.RegisterDeclaredBodyReader();
    }

    private static class RawStage {
        private static readonly ConcurrentDictionary<nint, Raw> Bodies = new();

        private static readonly Lazy<Stage> Installed = new Lazy<Stage>(() => {
            Stage stage = new Stage();
            stage.Register();
            return stage;
        });

        internal static void Install(MethodBase target, byte[] body, IReadOnlyList<OffsetPair> offsetMap) {
            RuntimeMethodHandle handle = WaylithClr.MethodBodyUpdater.GetDefinitionHandle(target);
            Bodies[handle.Value] = new Raw(body, offsetMap);
            Rebuild(handle);
        }

        internal static void Remove(MethodBase target) {
            if (target is DynamicMethod) {
                return;
            }

            RuntimeMethodHandle handle = WaylithClr.MethodBodyUpdater.GetDefinitionHandle(target);
            if (Bodies.TryRemove(handle.Value, out _)) {
                Rebuild(handle);
            }
        }

        internal static bool IsDefaultBody(MethodBase target) {
            WaylithClr.MethodBodyClause[] clauses = new WaylithClr.MethodBodyClause[64];
            return WaylithClr.MethodBodyUpdater.TryGetCurrentMethodBody(target, clauses, out WaylithClr.MethodBodyView body)
                && body.IsDefault;
        }

        private static void Rebuild(RuntimeMethodHandle handle) {
            if (Installed.Value.Rebuild(handle) is Exception failure) {
                throw failure;
            }
        }

        private sealed record Raw(byte[] Body, IReadOnlyList<OffsetPair> OffsetMap);

        private sealed class Stage : WaylithClr.MethodBodyTransformer {
            internal Stage() : base(RawPriority) {
            }

            protected override bool CanTransform(RuntimeMethodHandle method) {
                return Bodies.ContainsKey(method.Value);
            }

            protected override bool TryTransform(
                RuntimeMethodHandle method,
                in WaylithClr.MethodBodyView body,
                ReadOnlySpan<WaylithClr.MethodBodyClause> clauses,
                WaylithClr.MethodBodyBuilder result) {
                if (!Bodies.TryGetValue(method.Value, out Raw? raw)) {
                    return false;
                }

                result.Write(raw.Body);
                if (raw.OffsetMap.Count > 0) {
                    WaylithClr.ILOffsetMapEntry[] entries = new WaylithClr.ILOffsetMapEntry[raw.OffsetMap.Count];
                    for (int i = 0; i < entries.Length; i++) {
                        entries[i] = new WaylithClr.ILOffsetMapEntry(
                            raw.OffsetMap[i].Original,
                            raw.OffsetMap[i].Replaced);
                    }

                    result.AddOffsetMapEntries(entries);
                }

                return true;
            }
        }
    }
}
