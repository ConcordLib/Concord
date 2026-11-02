using System;
using System.Globalization;
using System.Reflection;

namespace Concord.Emit;

internal sealed class DeclaredView : MethodInfo {
    private readonly MethodBase original;

    private readonly MethodBody? declared;

    internal DeclaredView(MethodBase original, MethodBody? declared) {
        this.original = original;
        this.declared = declared;
    }

    public override Type ReturnType => original is MethodInfo method ? method.ReturnType : typeof(void);

    public override ParameterInfo ReturnParameter => ((MethodInfo)original).ReturnParameter;

    public override ICustomAttributeProvider ReturnTypeCustomAttributes =>
        ((MethodInfo)original).ReturnTypeCustomAttributes;

    public override RuntimeMethodHandle MethodHandle => original.MethodHandle;

    public override MethodAttributes Attributes => original.Attributes;

    public override CallingConventions CallingConvention => original.CallingConvention;

    public override Type? DeclaringType => original.DeclaringType;

    public override Type? ReflectedType => original.ReflectedType;

    public override Module Module => original.Module;

    public override int MetadataToken => original.MetadataToken;

    public override string Name => original.Name;

    public override bool IsGenericMethod => original.IsGenericMethod;

    public override bool IsGenericMethodDefinition => original.IsGenericMethodDefinition;

    public override bool ContainsGenericParameters => original.ContainsGenericParameters;

    public override MethodInfo GetBaseDefinition() {
        return original is MethodInfo method ? method.GetBaseDefinition() : this;
    }

    public override MethodBody? GetMethodBody() {
        return declared;
    }

    public override MethodImplAttributes GetMethodImplementationFlags() {
        return original.GetMethodImplementationFlags();
    }

    public override ParameterInfo[] GetParameters() {
        return original.GetParameters();
    }

    public override Type[] GetGenericArguments() {
        return original.GetGenericArguments();
    }

    public override MethodInfo GetGenericMethodDefinition() {
        return ((MethodInfo)original).GetGenericMethodDefinition();
    }

    public override MethodInfo MakeGenericMethod(params Type[] typeArguments) {
        return ((MethodInfo)original).MakeGenericMethod(typeArguments);
    }

    public override object? Invoke(
        object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? parameters, CultureInfo? culture) {
        return original.Invoke(obj, invokeAttr, binder, parameters, culture);
    }

    public override object[] GetCustomAttributes(bool inherit) {
        return original.GetCustomAttributes(inherit);
    }

    public override object[] GetCustomAttributes(Type attributeType, bool inherit) {
        return original.GetCustomAttributes(attributeType, inherit);
    }

    public override bool IsDefined(Type attributeType, bool inherit) {
        return original.IsDefined(attributeType, inherit);
    }

    public override string ToString() {
        return original.ToString()!;
    }

    public override bool Equals(object? obj) {
        return ReferenceEquals(this, obj) || original.Equals(obj) || (obj is DeclaredView view && original.Equals(view.original));
    }

    public override int GetHashCode() {
        return original.GetHashCode();
    }
}
