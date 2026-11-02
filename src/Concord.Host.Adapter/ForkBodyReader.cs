using System.Reflection;
using System.Reflection.Emit;

namespace Concord.Host.Adapter;

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
    public HostClause(int flags, int tryOffset, int tryLength, int handlerOffset, int handlerLength, int classTokenOrFilterOffset) {
        Flags = flags;
        TryOffset = tryOffset;
        TryLength = tryLength;
        HandlerOffset = handlerOffset;
        HandlerLength = handlerLength;
        ClassTokenOrFilterOffset = classTokenOrFilterOffset;
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
    public HostBody(
        byte[] il,
        byte[] localSignature,
        int localSignatureToken,
        int maxStackSize,
        bool initLocals,
        IReadOnlyList<HostClause> clauses) {
        Il = il;
        LocalSignature = localSignature;
        LocalSignatureToken = localSignatureToken;
        MaxStackSize = maxStackSize;
        InitLocals = initLocals;
        Clauses = clauses;
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
}

/// <summary>
///     Reads a method's original IL back from the host, bypassing <see cref="MethodBase.GetMethodBody" />, which on
///     a runtime that replaces bodies in place reports whatever body is live rather than the declared one.
/// </summary>
public sealed class ForkBodyReader {
    private const string OriginalEntry = "TryGetMethodBody";

    private const string ActiveEntry = "TryGetActiveMethodBody";

    private const int Scratch = 64;

    private const int Numbers = 4;

    private static readonly string[] ClauseFields = [
        "Flags", "TryOffset", "TryLength", "HandlerOffset", "HandlerLength", "ClassTokenOrFilterOffset",
    ];

    private readonly Func<MethodBase, Array, byte[][], int[], bool>? read;

    private readonly Func<MethodBase, Array, byte[][], int[], bool>? active;

    private readonly Type? clause;

    private readonly FieldInfo[]? fields;

    /// <summary>Initializes a new instance bound to whatever updater this runtime offers, if any.</summary>
    public ForkBodyReader() : this(RuntimeCapability.Updater()) {
    }

    /// <summary>Initializes a new instance.</summary>
    /// <param name="updater">The updater type to bind, or <see langword="null" /> for a reader that cannot read.</param>
    public ForkBodyReader(Type? updater) {
        MethodInfo? found = Find(updater);
        if (found is null) {
            return;
        }

        Type candidate = found.GetParameters()[1].ParameterType.GetGenericArguments()[0];
        FieldInfo[]? named = Fields(candidate);
        if (named is null) {
            return;
        }

        read = Bind(found, candidate);
        if (read is null) {
            return;
        }

        clause = candidate;
        fields = named;
        if (Find(updater, ActiveEntry) is { } live) {
            active = Bind(live, candidate);
        }
    }

    /// <summary>The process-wide reader, so one reflection bind serves every caller.</summary>
    public static ForkBodyReader Shared { get; } = new ForkBodyReader();

    /// <summary><see langword="true" /> when this reader bound and can answer. <see langword="false" /> off the fork.</summary>
    public bool IsAvailable => read is not null;

    /// <summary><see langword="true" /> when this reader can also report the body currently installed on a method.</summary>
    public bool ReadsActive => active is not null;

    /// <summary>Reads <paramref name="target" />'s original body, the one its metadata declares.</summary>
    /// <param name="target">The method to read.</param>
    /// <returns>The body, or <see langword="null" /> when this runtime has no such read or the host refused one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <see langword="null" />.</exception>
    public HostBody? Read(MethodBase target) {
        return Fetch(read, target);
    }

    /// <summary>Reads the body currently installed on <paramref name="target" />, replacements included.</summary>
    /// <param name="target">The method to read.</param>
    /// <returns>The body, or <see langword="null" /> when this runtime has no such read or the host refused one.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <see langword="null" />.</exception>
    public HostBody? ReadActive(MethodBase target) {
        return Fetch(active, target);
    }

    private static FieldInfo[]? Fields(Type clause) {
        FieldInfo[] found = new FieldInfo[ClauseFields.Length];
        for (int i = 0; i < ClauseFields.Length; i++) {
            FieldInfo? field = clause.GetField(ClauseFields[i], BindingFlags.Public | BindingFlags.Instance);
            if (field is null || field.FieldType != typeof(int)) {
                return null;
            }

            found[i] = field;
        }

        return found;
    }

    private static MethodInfo? Find(Type? updater) {
        return Find(updater, OriginalEntry);
    }

    private static MethodInfo? Find(Type? updater, string name) {
        if (updater is null) {
            return null;
        }

        foreach (MethodInfo candidate in updater.GetMethods(BindingFlags.Public | BindingFlags.Static)) {
            ParameterInfo[] parameters = candidate.GetParameters();
            if (candidate.Name == name &&
                parameters.Length == 3 &&
                parameters[0].ParameterType == typeof(MethodBase) &&
                parameters[1].ParameterType.IsGenericType &&
                parameters[1].ParameterType.GetGenericTypeDefinition() == typeof(Span<>) &&
                parameters[2].ParameterType.IsByRef) {
                return candidate;
            }
        }

        return null;
    }

    private static Func<MethodBase, Array, byte[][], int[], bool>? Bind(MethodInfo found, Type clause) {
        Type view = found.GetParameters()[2].ParameterType.GetElementType()!;
        MethodInfo? span = typeof(Span<>).MakeGenericType(clause).GetMethod("op_Implicit", [clause.MakeArrayType()]);
        MethodInfo? array = typeof(ReadOnlySpan<byte>).GetMethod(nameof(ReadOnlySpan<byte>.ToArray), Type.EmptyTypes);
        MethodInfo?[] blobs = [Getter(view, "IL"), Getter(view, "LocalSignature")];
        MethodInfo?[] numbers = [
            Getter(view, "LocalSignatureToken"), Getter(view, "MaxStackSize"),
            Getter(view, "InitLocals"), Getter(view, "ClauseCount"),
        ];
        if (span is null || array is null || blobs.Any(entry => entry is null) || numbers.Any(entry => entry is null)) {
            return null;
        }

        DynamicMethod call = new DynamicMethod(
            "ConcordReadOriginalBody",
            typeof(bool),
            [typeof(MethodBase), typeof(Array), typeof(byte[][]), typeof(int[])],
            typeof(ForkBodyReader).Module,
            true);

        ILGenerator gen = call.GetILGenerator();
        LocalBuilder held = gen.DeclareLocal(view);
        LocalBuilder bytes = gen.DeclareLocal(typeof(ReadOnlySpan<byte>));
        System.Reflection.Emit.Label absent = gen.DefineLabel();

        gen.Emit(OpCodes.Ldarg_0);
        gen.Emit(OpCodes.Ldarg_1);
        gen.Emit(OpCodes.Castclass, clause.MakeArrayType());
        gen.Emit(OpCodes.Call, span);
        gen.Emit(OpCodes.Ldloca, held);
        gen.Emit(OpCodes.Call, found);
        gen.Emit(OpCodes.Brfalse, absent);

        for (int i = 0; i < blobs.Length; i++) {
            gen.Emit(OpCodes.Ldarg_2);
            gen.Emit(OpCodes.Ldc_I4, i);
            gen.Emit(OpCodes.Ldloca, held);
            gen.Emit(OpCodes.Call, blobs[i]!);
            gen.Emit(OpCodes.Stloc, bytes);
            gen.Emit(OpCodes.Ldloca, bytes);
            gen.Emit(OpCodes.Call, array);
            gen.Emit(OpCodes.Stelem_Ref);
        }

        for (int i = 0; i < numbers.Length; i++) {
            gen.Emit(OpCodes.Ldarg_3);
            gen.Emit(OpCodes.Ldc_I4, i);
            gen.Emit(OpCodes.Ldloca, held);
            gen.Emit(OpCodes.Call, numbers[i]!);
            gen.Emit(OpCodes.Stelem_I4);
        }

        gen.Emit(OpCodes.Ldc_I4_1);
        gen.Emit(OpCodes.Ret);

        gen.MarkLabel(absent);
        gen.Emit(OpCodes.Ldc_I4_0);
        gen.Emit(OpCodes.Ret);

        try {
            return call.CreateDelegate<Func<MethodBase, Array, byte[][], int[], bool>>();
        } catch (Exception failure) when (failure is InvalidProgramException or BadImageFormatException or NotSupportedException) {
            return null;
        }
    }

    private static MethodInfo? Getter(Type view, string name) {
        return view.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod();
    }

    private HostBody? Fetch(Func<MethodBase, Array, byte[][], int[], bool>? entry, MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);

        if (entry is null || clause is null || fields is null) {
            return null;
        }

        Array scratch = Array.CreateInstance(clause, Scratch);
        byte[][] blobs = new byte[2][];
        int[] numbers = new int[Numbers];
        if (!entry(target, scratch, blobs, numbers)) {
            return null;
        }

        int count = numbers[3];
        if (count < 0 || count > Scratch) {
            return null;
        }

        HostClause[] clauses = new HostClause[count];
        for (int i = 0; i < count; i++) {
            object boxed = scratch.GetValue(i)!;
            clauses[i] = new HostClause(
                (int)fields[0].GetValue(boxed)!,
                (int)fields[1].GetValue(boxed)!,
                (int)fields[2].GetValue(boxed)!,
                (int)fields[3].GetValue(boxed)!,
                (int)fields[4].GetValue(boxed)!,
                (int)fields[5].GetValue(boxed)!);
        }

        return new HostBody(blobs[0] ?? [], blobs[1] ?? [], numbers[0], numbers[1], numbers[2] != 0, clauses);
    }
}
