using System.Reflection;

namespace Concord.Host.Adapter;

/// <summary>
///     Decodes an ECMA-335 <c>LocalVarSig</c> blob back into local slots, the inverse of
///     <see cref="LocalSignatureEncoder" />. Leaf tokens resolve in the method's own generic context.
/// </summary>
public static class LocalSignatureDecoder {
    private const byte LocalVarSig = 0x07;

    private const byte ElementVoid = 0x01;
    private const byte ElementBoolean = 0x02;
    private const byte ElementChar = 0x03;
    private const byte ElementI1 = 0x04;
    private const byte ElementU1 = 0x05;
    private const byte ElementI2 = 0x06;
    private const byte ElementU2 = 0x07;
    private const byte ElementI4 = 0x08;
    private const byte ElementU4 = 0x09;
    private const byte ElementI8 = 0x0a;
    private const byte ElementU8 = 0x0b;
    private const byte ElementR4 = 0x0c;
    private const byte ElementR8 = 0x0d;
    private const byte ElementString = 0x0e;
    private const byte ElementPointer = 0x0f;
    private const byte ElementByRef = 0x10;
    private const byte ElementValueType = 0x11;
    private const byte ElementClass = 0x12;
    private const byte ElementVar = 0x13;
    private const byte ElementArray = 0x14;
    private const byte ElementGenericInstance = 0x15;
    private const byte ElementTypedByRef = 0x16;
    private const byte ElementIntPtr = 0x18;
    private const byte ElementUIntPtr = 0x19;
    private const byte ElementFunctionPointer = 0x1b;
    private const byte ElementObject = 0x1c;
    private const byte ElementSzArray = 0x1d;
    private const byte ElementMvar = 0x1e;
    private const byte ElementModifierRequired = 0x1f;
    private const byte ElementModifierOptional = 0x20;
    private const byte ElementPinned = 0x45;

    private static readonly int[] TypeTables = [0x02000000, 0x01000000, 0x1b000000, 0x1b000000];

    /// <summary>Decodes <paramref name="signature" /> into slots in slot order.</summary>
    /// <param name="signature">A <c>LocalVarSig</c> blob. An empty blob decodes to no slots.</param>
    /// <param name="module">The module the blob's type tokens belong to.</param>
    /// <param name="typeArguments">The declaring type's generic arguments, or <see langword="null" /> when it has none.</param>
    /// <param name="methodArguments">The method's own generic arguments, or <see langword="null" /> when it has none.</param>
    /// <param name="refusal">Why the blob could not be decoded, or <see langword="null" /> on success.</param>
    /// <returns>The slots, or <see langword="null" /> when <paramref name="refusal" /> says why not.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signature" /> or <paramref name="module" /> is <c>null</c>.</exception>
    public static ComposedLocal[]? Decode(
        byte[] signature,
        Module module,
        Type[]? typeArguments,
        Type[]? methodArguments,
        out string? refusal) {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(module);

        refusal = null;
        if (signature.Length == 0) {
            return [];
        }

        try {
            return Read(signature, module, typeArguments, methodArguments, out refusal);
        } catch (Exception malformed) when (malformed is IndexOutOfRangeException or ArgumentOutOfRangeException
                                                or BadImageFormatException or InvalidOperationException) {
            refusal = "LocalVarSig " + malformed.GetType().Name;
            return null;
        } catch (Exception unresolved) when (unresolved is ArgumentException or TypeLoadException
                                                 or FileNotFoundException or MissingMemberException) {
            refusal = "LocalVarSig token " + unresolved.GetType().Name;
            return null;
        }
    }

    private static ComposedLocal[]? Read(
        byte[] signature,
        Module module,
        Type[]? typeArguments,
        Type[]? methodArguments,
        out string? refusal) {
        refusal = null;
        Cursor cursor = new Cursor(signature);
        byte header = cursor.Byte();
        if (header != LocalVarSig) {
            refusal = $"LocalVarSig header is 0x{header:x2}, not 0x{LocalVarSig:x2}";
            return null;
        }

        int count = cursor.Compressed();
        if (count < 1 || count > LocalSignatureEncoder.MaxLocals) {
            refusal = $"LocalVarSig declares {count} locals";
            return null;
        }

        Context context = new Context(module, typeArguments, methodArguments);
        ComposedLocal[] locals = new ComposedLocal[count];
        for (int slot = 0; slot < count; slot++) {
            if (!Slot(ref cursor, context, slot, out ComposedLocal local, out refusal)) {
                return null;
            }

            locals[slot] = local;
        }

        return locals;
    }

    private static bool Slot(ref Cursor cursor, Context context, int slot, out ComposedLocal local, out string? refusal) {
        local = default;
        refusal = null;
        bool pinned = false;

        while (true) {
            byte code = cursor.Byte();
            switch (code) {
                case ElementPinned:
                    pinned = true;
                    continue;
                case ElementModifierRequired:
                case ElementModifierOptional:
                    cursor.Compressed();
                    continue;
                case ElementTypedByRef:
                    local = new ComposedLocal(typeof(TypedReference), false);
                    return true;
                case ElementByRef: {
                    Type? referent = Decode(ref cursor, context, slot, out refusal);
                    if (referent is null) {
                        return false;
                    }

                    local = new ComposedLocal(referent.MakeByRefType(), pinned);
                    return true;
                }

                default: {
                    Type? held = Decode(ref cursor, context, slot, code, out refusal);
                    if (held is null) {
                        return false;
                    }

                    local = new ComposedLocal(held, pinned);
                    return true;
                }
            }
        }
    }

    private static Type? Decode(ref Cursor cursor, Context context, int slot, out string? refusal) {
        return Decode(ref cursor, context, slot, cursor.Byte(), out refusal);
    }

    private static Type? Decode(ref Cursor cursor, Context context, int slot, byte code, out string? refusal) {
        refusal = null;
        switch (code) {
            case ElementVoid: return typeof(void);
            case ElementBoolean: return typeof(bool);
            case ElementChar: return typeof(char);
            case ElementI1: return typeof(sbyte);
            case ElementU1: return typeof(byte);
            case ElementI2: return typeof(short);
            case ElementU2: return typeof(ushort);
            case ElementI4: return typeof(int);
            case ElementU4: return typeof(uint);
            case ElementI8: return typeof(long);
            case ElementU8: return typeof(ulong);
            case ElementR4: return typeof(float);
            case ElementR8: return typeof(double);
            case ElementIntPtr: return typeof(nint);
            case ElementUIntPtr: return typeof(nuint);
            case ElementString: return typeof(string);
            case ElementObject: return typeof(object);
            case ElementValueType:
            case ElementClass:
                return context.Resolve(cursor.TypeToken(), slot, out refusal);
            case ElementVar:
                return context.TypeParameter(cursor.Compressed(), slot, out refusal);
            case ElementMvar:
                return context.MethodParameter(cursor.Compressed(), slot, out refusal);
            case ElementPointer: {
                Type? target = Decode(ref cursor, context, slot, out refusal);
                return target?.MakePointerType();
            }

            case ElementSzArray: {
                Type? element = Decode(ref cursor, context, slot, out refusal);
                return element?.MakeArrayType();
            }

            case ElementArray:
                return Shaped(ref cursor, context, slot, out refusal);
            case ElementGenericInstance:
                return Instantiation(ref cursor, context, slot, out refusal);
            case ElementFunctionPointer:
                refusal = $"local slot {slot} holds a function pointer, which has no Type to decode to";
                return null;
            default:
                refusal = $"local slot {slot} carries the unhandled element type 0x{code:x2}";
                return null;
        }
    }

    private static Type? Shaped(ref Cursor cursor, Context context, int slot, out string? refusal) {
        Type? element = Decode(ref cursor, context, slot, out refusal);
        if (element is null) {
            return null;
        }

        int rank = cursor.Compressed();
        int sizes = cursor.Compressed();
        for (int i = 0; i < sizes; i++) {
            cursor.Compressed();
        }

        int bounds = cursor.Compressed();
        for (int i = 0; i < bounds; i++) {
            cursor.Signed();
        }

        if (rank < 1) {
            refusal = $"local slot {slot} declares an array of rank {rank}";
            return null;
        }

        return element.MakeArrayType(rank);
    }

    private static Type? Instantiation(ref Cursor cursor, Context context, int slot, out string? refusal) {
        cursor.Byte();
        Type? definition = context.Resolve(cursor.TypeToken(), slot, out refusal);
        if (definition is null) {
            return null;
        }

        int count = cursor.Compressed();
        Type[] arguments = new Type[count];
        for (int i = 0; i < count; i++) {
            Type? argument = Decode(ref cursor, context, slot, out refusal);
            if (argument is null) {
                return null;
            }

            arguments[i] = argument;
        }

        int declared = definition.IsGenericTypeDefinition ? definition.GetGenericArguments().Length : -1;
        if (declared != count) {
            refusal = $"local slot {slot}: '{definition}' declares {declared} generic arguments and the " +
                $"signature supplies {count}";
            return null;
        }

        return definition.MakeGenericType(arguments);
    }

    private struct Cursor {
        private readonly byte[] blob;

        private int at;

        internal Cursor(byte[] blob) {
            this.blob = blob;
            at = 0;
        }

        internal byte Byte() {
            return blob[at++];
        }

        internal int Compressed() {
            byte first = Byte();
            if ((first & 0x80) == 0) {
                return first;
            }

            if ((first & 0xC0) == 0x80) {
                return ((first & 0x3F) << 8) | Byte();
            }

            if ((first & 0xE0) == 0xC0) {
                return ((first & 0x1F) << 24) | (Byte() << 16) | (Byte() << 8) | Byte();
            }

            throw new BadImageFormatException($"0x{first:x2} starts no valid compressed integer");
        }

        internal int Signed() {
            int packed = Compressed();
            int value = packed >> 1;
            return (packed & 1) == 0 ? value : value - (1 << 30);
        }

        internal int TypeToken() {
            int encoded = Compressed();
            return TypeTables[encoded & 3] | (encoded >> 2);
        }
    }

    private sealed class Context {
        private readonly Module module;

        private readonly Type[]? typeArguments;

        private readonly Type[]? methodArguments;

        internal Context(Module module, Type[]? typeArguments, Type[]? methodArguments) {
            this.module = module;
            this.typeArguments = typeArguments;
            this.methodArguments = methodArguments;
        }

        internal Type? Resolve(int token, int slot, out string? refusal) {
            refusal = null;
            Type? resolved = module.ResolveType(token, typeArguments, methodArguments);
            if (resolved is not null) {
                return resolved;
            }

            refusal = $"local slot {slot}: token 0x{token:x8} resolves to no type in '{module.Name}'";
            return null;
        }

        internal Type? TypeParameter(int position, int slot, out string? refusal) {
            return At(typeArguments, position, slot, "declaring type", out refusal);
        }

        internal Type? MethodParameter(int position, int slot, out string? refusal) {
            return At(methodArguments, position, slot, "method", out refusal);
        }

        private static Type? At(Type[]? arguments, int position, int slot, string owner, out string? refusal) {
            refusal = null;
            if (arguments is not null && position >= 0 && position < arguments.Length) {
                return arguments[position];
            }

            refusal = $"local slot {slot} names generic parameter {position} of the {owner}, which supplies " +
                $"{arguments?.Length ?? 0}";
            return null;
        }
    }
}
