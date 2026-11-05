using System.Reflection;

namespace Concord.Emit;

internal static class StockBody {
    internal const string RefusalCode = "CONC181";

    internal static ComposedBody Read(MethodBase method) {
        MethodBody? body = method.GetMethodBody()
            ?? throw Refuse(method, "reflection reports no body for it");
        byte[] il = body.GetILAsByteArray()
            ?? throw Refuse(method, "reflection reports no IL for it");
        byte[] signature = body.LocalSignatureMetadataToken != 0
            ? method.Module.ResolveSignature(body.LocalSignatureMetadataToken)
            : [];

        List<HostClause> clauses = new List<HostClause>(body.ExceptionHandlingClauses.Count);
        foreach (ExceptionHandlingClause clause in body.ExceptionHandlingClauses) {
            bool caught = clause.Flags == ExceptionHandlingClauseOptions.Clause;
            clauses.Add(new HostClause(
                (int)clause.Flags,
                clause.TryOffset,
                clause.TryLength,
                clause.HandlerOffset,
                clause.HandlerLength,
                clause.Flags == ExceptionHandlingClauseOptions.Filter ? clause.FilterOffset : 0,
                caught ? clause.CatchType : null));
        }

        List<ComposedLocal> locals = new List<ComposedLocal>(body.LocalVariables.Count);
        foreach (LocalVariableInfo local in body.LocalVariables) {
            locals.Add(new ComposedLocal(local.LocalType, local.IsPinned));
        }

        HostBody host = new HostBody(
            il,
            signature,
            body.LocalSignatureMetadataToken,
            body.MaxStackSize,
            body.InitLocals,
            clauses,
            locals);

        return BodyDecoder.Decode(method, host, out string? refusal)
            ?? throw Refuse(method, refusal ?? "its body could not be decoded");
    }

    private static ConcordEmitException Refuse(MethodBase method, string because) {
        return new ConcordEmitException(
            RefusalCode,
            $"Concord cannot read the body of '{method.DeclaringType?.FullName ?? "<no declaring type>"}.{method.Name}': {because}.");
    }
}
