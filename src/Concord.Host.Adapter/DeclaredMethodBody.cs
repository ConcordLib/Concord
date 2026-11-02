using System.Reflection;
using Concord.Emit;

namespace Concord.Host.Adapter;

/// <summary>A declared body in reflection's shape, so a consumer that reads IL itself sees the declared stream.</summary>
public sealed class DeclaredMethodBody : MethodBody {
    private readonly byte[] il;

    private readonly LocalVariableInfo[] locals;

    private readonly ExceptionHandlingClause[] clauses;

    private readonly int maxStack;

    private readonly bool initLocals;

    private readonly int localsToken;

    internal DeclaredMethodBody(
        byte[] il,
        LocalVariableInfo[] locals,
        ExceptionHandlingClause[] clauses,
        int maxStack,
        bool initLocals,
        int localsToken) {
        this.il = il;
        this.locals = locals;
        this.clauses = clauses;
        this.maxStack = maxStack;
        this.initLocals = initLocals;
        this.localsToken = localsToken;
    }

    /// <inheritdoc />
    public override IList<LocalVariableInfo> LocalVariables => locals;

    /// <inheritdoc />
    public override IList<ExceptionHandlingClause> ExceptionHandlingClauses => clauses;

    /// <inheritdoc />
    public override int MaxStackSize => maxStack;

    /// <inheritdoc />
    public override bool InitLocals => initLocals;

    /// <inheritdoc />
    public override int LocalSignatureMetadataToken => localsToken;

    /// <inheritdoc />
    public override byte[] GetILAsByteArray() {
        return (byte[])il.Clone();
    }

    internal static DeclaredMethodBody Build(MethodBase method, HostBody read, ComposedLocal[] decoded) {
        Type[]? typeArguments = method.DeclaringType is { IsGenericType: true } declaring
            ? declaring.GetGenericArguments()
            : null;
        Type[]? methodArguments = method is MethodInfo { IsGenericMethod: true } generic
            ? generic.GetGenericArguments()
            : null;

        LocalVariableInfo[] locals = new LocalVariableInfo[decoded.Length];
        for (int i = 0; i < decoded.Length; i++) {
            locals[i] = new DeclaredLocal(i, decoded[i].Type, decoded[i].IsPinned);
        }

        ExceptionHandlingClause[] clauses = new ExceptionHandlingClause[read.Clauses.Count];
        for (int i = 0; i < clauses.Length; i++) {
            HostClause clause = read.Clauses[i];
            Type? caught = clause.Flags == 0
                ? method.Module.ResolveType(clause.ClassTokenOrFilterOffset, typeArguments, methodArguments)
                : null;

            clauses[i] = new DeclaredClause(clause, caught);
        }

        return new DeclaredMethodBody(
            read.Il.ToArray(), locals, clauses, read.MaxStackSize, read.InitLocals, read.LocalSignatureToken);
    }

    private sealed class DeclaredLocal : LocalVariableInfo {
        private readonly int slot;

        private readonly Type type;

        private readonly bool pinned;

        internal DeclaredLocal(int slot, Type type, bool pinned) {
            this.slot = slot;
            this.type = type;
            this.pinned = pinned;
        }

        public override int LocalIndex => slot;

        public override Type LocalType => type;

        public override bool IsPinned => pinned;
    }

    private sealed class DeclaredClause : ExceptionHandlingClause {
        private readonly HostClause clause;

        private readonly Type? caught;

        internal DeclaredClause(HostClause clause, Type? caught) {
            this.clause = clause;
            this.caught = caught;
        }

        public override ExceptionHandlingClauseOptions Flags => (ExceptionHandlingClauseOptions)clause.Flags;

        public override int TryOffset => clause.TryOffset;

        public override int TryLength => clause.TryLength;

        public override int HandlerOffset => clause.HandlerOffset;

        public override int HandlerLength => clause.HandlerLength;

        public override Type CatchType => caught!;

        public override int FilterOffset => clause.ClassTokenOrFilterOffset;
    }
}
