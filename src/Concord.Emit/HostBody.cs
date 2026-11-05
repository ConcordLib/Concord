namespace Concord.Emit;

/// <summary>
///     One exception-handling region of a body the host read back, in raw ECMA-335 terms.
/// </summary>
public readonly struct HostClause {
    /// <summary>Initializes a new instance.</summary>
    /// <param name="flags">The clause kind: 0 catch, 1 filter, 2 finally, 4 fault.</param>
    /// <param name="tryOffset">The byte offset the protected range starts at.</param>
    /// <param name="tryLength">The protected range's length in bytes.</param>
    /// <param name="handlerOffset">The byte offset the handler starts at.</param>
    /// <param name="handlerLength">The handler's length in bytes.</param>
    /// <param name="classTokenOrFilterOffset">A catch's type token, or a filter's start offset.</param>
    /// <param name="caughtType">The caught type already resolved, for a reader whose source names types rather than tokens.</param>
    public HostClause(
        int flags,
        int tryOffset,
        int tryLength,
        int handlerOffset,
        int handlerLength,
        int classTokenOrFilterOffset,
        Type? caughtType = null) {
        Flags = flags;
        TryOffset = tryOffset;
        TryLength = tryLength;
        HandlerOffset = handlerOffset;
        HandlerLength = handlerLength;
        ClassTokenOrFilterOffset = classTokenOrFilterOffset;
        CaughtType = caughtType;
    }

    /// <summary>The clause kind: 0 catch, 1 filter, 2 finally, 4 fault.</summary>
    public int Flags { get; }

    /// <summary>The byte offset the protected range starts at.</summary>
    public int TryOffset { get; }

    /// <summary>The protected range's length in bytes.</summary>
    public int TryLength { get; }

    /// <summary>The byte offset the handler starts at.</summary>
    public int HandlerOffset { get; }

    /// <summary>The handler's length in bytes.</summary>
    public int HandlerLength { get; }

    /// <summary>A catch's type token, or a filter's start offset.</summary>
    public int ClassTokenOrFilterOffset { get; }

    /// <summary>The caught type, when the reader resolved it itself. Null leaves <see cref="ClassTokenOrFilterOffset" /> as the answer.</summary>
    public Type? CaughtType { get; }
}

/// <summary>
///     A method body the host read back: raw IL plus the header values needed to rebuild it.
/// </summary>
public sealed class HostBody {
    /// <summary>Initializes a new instance.</summary>
    /// <param name="il">The body's IL stream, with no method header on it.</param>
    /// <param name="localSignature">The body's <c>LocalVarSig</c> blob, empty when it declares no locals.</param>
    /// <param name="localSignatureToken">The standalone signature token the locals came from, or 0 for none.</param>
    /// <param name="maxStackSize">The header's declared maxstack.</param>
    /// <param name="initLocals">Whether the header zero-initializes the local slots.</param>
    /// <param name="clauses">The exception-handling regions, in header order.</param>
    /// <param name="locals">The local slots already resolved, for a reader whose source names types rather than a signature blob.</param>
    public HostBody(
        byte[] il,
        byte[] localSignature,
        int localSignatureToken,
        int maxStackSize,
        bool initLocals,
        IReadOnlyList<HostClause> clauses,
        IReadOnlyList<ComposedLocal>? locals = null) {
        Il = il;
        LocalSignature = localSignature;
        LocalSignatureToken = localSignatureToken;
        MaxStackSize = maxStackSize;
        InitLocals = initLocals;
        Clauses = clauses;
        Locals = locals;
    }

    /// <summary>The body's IL stream, with no method header on it.</summary>
    public byte[] Il { get; }

    /// <summary>The body's <c>LocalVarSig</c> blob, empty when it declares no locals.</summary>
    public byte[] LocalSignature { get; }

    /// <summary>The standalone signature token the locals came from, or 0 for none.</summary>
    public int LocalSignatureToken { get; }

    /// <summary>The header's declared maxstack.</summary>
    public int MaxStackSize { get; }

    /// <summary>Whether the header zero-initializes the local slots.</summary>
    public bool InitLocals { get; }

    /// <summary>The exception-handling regions, in header order.</summary>
    public IReadOnlyList<HostClause> Clauses { get; }

    /// <summary>The local slots when the reader resolved them itself. Null leaves <see cref="LocalSignature" /> as the answer.</summary>
    public IReadOnlyList<ComposedLocal>? Locals { get; }
}
