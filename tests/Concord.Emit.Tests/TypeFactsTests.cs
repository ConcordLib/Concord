using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public struct FnPtrFieldHolder {
    public unsafe delegate*<int, int> Fp;
}

public struct NestedFnPtrFieldHolder {
    public FnPtrFieldHolder Inner;
}

public ref struct ScratchHolder {
    public int Value;
}

public static class FunctionPointerShapes {
    public static unsafe int ByParameter(delegate*<int, int> fp, int seed) => fp(seed);

    public static unsafe delegate*<int, int> ByReturn() => &Step;

    public static unsafe int ByLocal(int seed) {
        delegate*<int, int> fp = &Step;
        return fp(seed);
    }

    private static int Step(int value) => value + 1;
}

public sealed class TypeFactsTests {
    [Theory]
    [InlineData("int", true)]
    [InlineData("void", true)]
    [InlineData("nullable", true)]
    [InlineData("enum", true)]
    [InlineData("open-struct", true)]
    [InlineData("ref-struct", true)]
    [InlineData("fnptr-field-struct", true)]
    [InlineData("nested-fnptr-field-struct", true)]
    [InlineData("system-enum", false)]
    [InlineData("system-valuetype", false)]
    [InlineData("object", false)]
    [InlineData("string", false)]
    [InlineData("delegate", false)]
    [InlineData("interface", false)]
    [InlineData("array", false)]
    [InlineData("pointer", false)]
    [InlineData("byref", false)]
    [InlineData("generic-parameter", false)]
    public void IsValueTypeAgreesWithTheRuntime(string shape, bool expected) {
        Assert.Equal(expected, TypeFacts.IsValueType(Resolve(shape)));
    }

    [MonoOnlyTheory]
    [InlineData("param")]
    [InlineData("return")]
    [InlineData("local")]
    public void MonoFunctionPointerIsRecognisedWithoutReadingItsAttributes(string position) {
        Type functionPointer = FunctionPointerAt(position);

        Assert.True(TypeFacts.IsFunctionPointer(functionPointer));
        Assert.False(TypeFacts.IsByRefLike(functionPointer));
        Assert.False(TypeFacts.IsValueType(functionPointer));
    }

    private static Type FunctionPointerAt(string position) {
        return position switch {
            "param" => typeof(FunctionPointerShapes).GetMethod(nameof(FunctionPointerShapes.ByParameter))!.GetParameters()[0].ParameterType,
            "return" => typeof(FunctionPointerShapes).GetMethod(nameof(FunctionPointerShapes.ByReturn))!.ReturnType,
            "local" => typeof(FunctionPointerShapes).GetMethod(nameof(FunctionPointerShapes.ByLocal))!.GetMethodBody()!.LocalVariables[0].LocalType,
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, "unknown function-pointer position"),
        };
    }

    private static Type Resolve(string shape) {
        return shape switch {
            "int" => typeof(int),
            "void" => typeof(void),
            "nullable" => typeof(int?),
            "enum" => typeof(DayOfWeek),
            "open-struct" => typeof(KeyValuePair<,>),
            "ref-struct" => typeof(ScratchHolder),
            "fnptr-field-struct" => Type.GetType("Concord.Emit.Tests.FnPtrFieldHolder", true)!,
            "nested-fnptr-field-struct" => Type.GetType("Concord.Emit.Tests.NestedFnPtrFieldHolder", true)!,
            "system-enum" => typeof(Enum),
            "system-valuetype" => typeof(ValueType),
            "object" => typeof(object),
            "string" => typeof(string),
            "delegate" => typeof(Action),
            "interface" => typeof(IDisposable),
            "array" => typeof(int[]),
            "pointer" => typeof(int).MakePointerType(),
            "byref" => typeof(int).MakeByRefType(),
            "generic-parameter" => typeof(List<>).GetGenericArguments()[0],
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "unknown type shape"),
        };
    }
}
