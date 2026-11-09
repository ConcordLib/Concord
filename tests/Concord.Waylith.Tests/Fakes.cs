using System.Reflection;

namespace Concord.Waylith.Tests;

internal sealed class Tokens : ITokenSource {
    internal const int LocalsToken = 0x11000001;

    private readonly Dictionary<object, int> assigned = new Dictionary<object, int>();

    public byte[]? Locals { get; private set; }

    public int TokenFor(Type type) {
        return Assign(type, 0x01000000);
    }

    public int TokenFor(FieldInfo field) {
        return Assign(field, 0x04000000);
    }

    public int TokenFor(MethodBase method) {
        return Assign(method, 0x0a000000);
    }

    public int TokenFor(string literal) {
        return Assign(literal, 0x70000000);
    }

    public int TokenForLocals(byte[] signature) {
        Locals = signature;
        return LocalsToken;
    }

    private int Assign(object key, int table) {
        if (!assigned.TryGetValue(key, out int token)) {
            token = table | (assigned.Count + 1);
            assigned.Add(key, token);
        }

        return token;
    }
}

public sealed class Held<T> {
    public int Ping() {
        return 1;
    }
}

internal sealed class NoTokens : ITokenSource {
    public int TokenFor(MethodBase method) => throw Refused(method);

    public int TokenFor(FieldInfo field) => throw Refused(field);

    public int TokenFor(Type type) => throw Refused(type);

    public int TokenFor(string literal) => throw Refused(literal);

    public int TokenForLocals(byte[] signature) => throw Refused(signature);

    private static InvalidOperationException Refused(object subject) {
        return new InvalidOperationException($"A body of 'ldc.i4 n; ret' asked for a token for '{subject}', so the encoder is minting something this test did not write.");
    }
}

internal sealed class FixedStack : IMaxStackSource {
    private readonly int value;

    internal FixedStack(int value) {
        this.value = value;
    }

    internal ComposedBody? Asked { get; private set; }

    public int MaxStackFor(ComposedBody body) {
        Asked = body;
        return value;
    }
}
