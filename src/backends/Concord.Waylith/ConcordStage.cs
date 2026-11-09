using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using WaylithClr;

namespace Concord.Waylith;

internal sealed class ConcordStage : MethodBodyTransformer {
    internal const int StagePriority = -1000;

    internal const string DecodeCode = "CONC180";

    private const string EvictedMarker = "[Concord.Waylith] injection-evicted";

    private static readonly ComputedStack Stack = new ComputedStack();

    private readonly StagePublisher publisher;

    private ConcordStage(StagePublisher owner) : base(StagePriority) {
        publisher = owner;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static StagePublisher Install() {
        StagePublisher owner = new StagePublisher(
            ForkApi.DefinitionHandle,
            static message => WaylithClrBackend.Log(message));
        ConcordStage stage = new ConcordStage(owner);
        owner.UseRebuild(stage.Rebuild);
        AdapterEntryPoint.DefinitionHandle = ForkApi.DefinitionHandle;
        stage.Register();
        return owner;
    }

    protected override bool CanTransform(RuntimeMethodHandle method) {
        return publisher.Staged(method) is not null;
    }

    protected override bool TryTransform(
        RuntimeMethodHandle method,
        in MethodBodyView body,
        ReadOnlySpan<MethodBodyClause> clauses,
        MethodBodyBuilder result) {
        if (publisher.Staged(method) is not StagedTarget staged) {
            return false;
        }

        MethodBase target = staged.Target;
        HostBody input = ForkApi.Copy(in body, clauses)
            ?? throw Refuse(target, "the runtime reported more exception clauses than it handed over");

        ComposedBody decoded = BodyDecoder.Decode(target, input, out string? refusal)
            ?? throw Refuse(target, "Concord could not decode the body this stage was handed: " + refusal);

        StageInputSlot? outer = AdapterEntryPoint.StageInput(method, decoded);
        try {
            ComposeResult composed = WrapperComposer.Compose(target, staged.Ordered);
            foreach (RejectedInjection rejection in composed.Rejected) {
                if (EvictionReport.Compose(EvictedMarker, target, rejection) is string report) {
                    publisher.Report(report);
                }
            }

            if (composed.Body is null) {
                throw Refuse(
                    target,
                    "the compose rejected its body: " + (composed.BodyRejection ?? "no reason was reported"));
            }

            EncodedBody encoded = BodyEncoder.Encode(
                composed.Body,
                new WaylithTokenSource(target.Module, new BuilderMinter(result)),
                Stack);

            int band = WaylithClrBackend.Band(target, encoded, input.Il.Length);
            IReadOnlyList<OffsetPair> map = WaylithClrBackend.OffsetMap(composed.Body, encoded, band);

            result.Write(encoded.Bytes);
            FrameAttribution.Register(target, composed.Body, encoded, staged.Ordered, band);
            if (map.Count == 0) {
                if (target is not DynamicMethod) {
                    publisher.Report(
                        WaylithClrBackend.NoOffsetMapWarning(
                            target,
                            WaylithClrBackend.DistinctOwners(staged.Ordered).Count));
                }

                return true;
            }

            result.AddOffsetMapEntries(Entries(map));
            return true;
        } finally {
            AdapterEntryPoint.RestoreStageInput(outer);
        }
    }

    protected override void OnTransformFailed(RuntimeMethodHandle method, Exception exception) {
        publisher.Failed(method, exception);
    }

    protected override void OnDefaultBodiesChanged(ReadOnlySpan<RuntimeMethodHandle> methods) {
        publisher.DefaultBodiesChanged(methods);
    }

    private static ILOffsetMapEntry[] Entries(IReadOnlyList<OffsetPair> map) {
        ILOffsetMapEntry[] entries = new ILOffsetMapEntry[map.Count];
        for (int i = 0; i < map.Count; i++) {
            entries[i] = new ILOffsetMapEntry(map[i].Original, map[i].Replaced);
        }

        return entries;
    }

    private static ConcordEmitException Refuse(MethodBase target, string because) {
        return new ConcordEmitException(
            DecodeCode,
            "'" + WaylithClrBackend.Describe(target) + "' cannot be staged: " + because.TrimEnd('.') + ".");
    }

    private sealed class BuilderMinter : IMetadataMinter {
        private readonly MethodBodyBuilder result;

        internal BuilderMinter(MethodBodyBuilder builder) {
            result = builder;
        }

        public int Token(Module target, MethodBase method) {
            return result.GetTokenFor(method);
        }

        public int Token(Module target, FieldInfo field) {
            return result.GetTokenFor(field);
        }

        public int Token(Module target, Type type) {
            return result.GetTokenFor(type);
        }

        public int Token(Module target, string literal) {
            return result.GetTokenFor(literal);
        }

        public int LocalsToken(Module target, byte[] signature) {
            return result.GetTokenForSignature(signature);
        }
    }
}
