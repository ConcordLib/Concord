using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Concord.Orchestration;
using WaylithClr;

namespace Concord.Waylith;

internal static class AccessCheckProbe {
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Exception? Run() {
        MethodInfo target = typeof(AccessCheckProbe).GetMethod(nameof(Reached), Hidden)!;
        Stage stage = new Stage(ForkApi.DefinitionHandle(target));
        stage.Register();
        try {
            if (stage.Rebuild(stage.Target) is Exception failure) {
                return failure;
            }

            return Reached() ? null : new InvalidOperationException("the staged probe body did not run");
        } finally {
            stage.Unregister();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Reached() {
        return false;
    }

    private static CodeInstruction[] Body() {
        Type scanner = typeof(PatchDeclarationScanner);
        FieldInfo flag = Find(typeof(ExtendedEnumRegistry).GetField("warnedNoStore", Hidden));
        FieldInfo elapsed = Find(typeof(Stopwatch).GetField("_elapsed", Hidden));
        Type closure = Find(typeof(Patcher).GetNestedTypes(BindingFlags.NonPublic).FirstOrDefault(static nested =>
            nested.Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal)
            && nested.GetConstructor(Type.EmptyTypes) is not null
            && nested.GetFields().Any(static field => !field.FieldType.IsValueType)
            && nested.GetMethods(Hidden | BindingFlags.DeclaredOnly).Length > 0));

        return [
            new CodeInstruction(OpCodes.Ldstr, "CONC061"),
            new CodeInstruction(OpCodes.Call, Find(scanner.GetMethod("IsDeclarationScopedCode", Hidden, [typeof(string)]))),
            new CodeInstruction(OpCodes.Pop),
            new CodeInstruction(OpCodes.Ldstr, "System.Int32"),
            new CodeInstruction(OpCodes.Call, Find(scanner.GetMethod("ResolveTypeByName", Hidden, [typeof(string)]))),
            new CodeInstruction(OpCodes.Pop),
            new CodeInstruction(OpCodes.Ldsfld, flag),
            new CodeInstruction(OpCodes.Stsfld, flag),
            new CodeInstruction(OpCodes.Newobj, typeof(Stopwatch).GetConstructor(Type.EmptyTypes)!),
            new CodeInstruction(OpCodes.Dup),
            new CodeInstruction(OpCodes.Ldfld, elapsed),
            new CodeInstruction(OpCodes.Stfld, elapsed),
            new CodeInstruction(OpCodes.Newobj, closure.GetConstructor(Type.EmptyTypes)!),
            new CodeInstruction(OpCodes.Ldnull),
            new CodeInstruction(OpCodes.Stfld, closure.GetFields().First(static field => !field.FieldType.IsValueType)),
            new CodeInstruction(OpCodes.Ldftn, closure.GetMethods(Hidden | BindingFlags.DeclaredOnly)[0]),
            new CodeInstruction(OpCodes.Pop),
            new CodeInstruction(OpCodes.Ldtoken, Find(scanner.Assembly.GetType("Concord.Orchestration.PatchHandle"))),
            new CodeInstruction(OpCodes.Pop),
            new CodeInstruction(OpCodes.Ldc_I4_1),
            new CodeInstruction(OpCodes.Ret),
        ];
    }

    private static T Find<T>(T? member)
        where T : MemberInfo {
        return member ?? throw new MissingMemberException("a member the access check probe names is not in this build");
    }

    private sealed class Stage : MethodBodyTransformer {
        internal Stage(RuntimeMethodHandle target) : base(ConcordStage.StagePriority) {
            Target = target;
        }

        internal RuntimeMethodHandle Target { get; }

        protected override bool CanTransform(RuntimeMethodHandle method) {
            return method.Equals(Target);
        }

        protected override bool TryTransform(
            RuntimeMethodHandle method,
            in MethodBodyView body,
            ReadOnlySpan<MethodBodyClause> clauses,
            MethodBodyBuilder result) {
            ComposedBody composed = new ComposedBody(
                [.. Body().Select(static instruction => new ComposedInstruction(instruction, null, null))], [], [], false);
            result.Write(BodyEncoder.Encode(composed, new Ungated(result), new ComputedStack()).Bytes);
            return true;
        }
    }

    private sealed class Ungated : ITokenSource {
        private readonly MethodBodyBuilder result;

        internal Ungated(MethodBodyBuilder builder) {
            result = builder;
        }

        public int TokenFor(Type type) => result.GetTokenFor(type);

        public int TokenFor(FieldInfo field) => result.GetTokenFor(field);

        public int TokenFor(MethodBase method) => result.GetTokenFor(method);

        public int TokenFor(string literal) => result.GetTokenFor(literal);

        public int TokenForLocals(byte[] signature) => result.GetTokenForSignature(signature);
    }
}
