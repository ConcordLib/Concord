using System.Reflection.Metadata;
using Concord.Emit;
using CallSite = Mono.Cecil.CallSite;

namespace Concord;

/// <summary>
///     A <c>calli</c> call-site signature: its return type, parameter types and calling convention. A transpiler
///     can move or delete a <c>calli</c>, but constructing a new signature is not supported in this version.
/// </summary>
public sealed class CallSiteSignature {
    internal CallSiteSignature(CallSite site) {
        Type[] taken = new Type[site.Parameters.Count];
        for (int i = 0; i < taken.Length; i++) {
            taken[i] = site.Parameters[i].ParameterType.ResolveReflection();
        }

        ReturnType = site.ReturnType.ResolveReflection();
        Parameters = taken;
        Convention = (SignatureCallingConvention)(int)site.CallingConvention;
        HasThis = site.HasThis;
        ExplicitThis = site.ExplicitThis;
    }

    internal CallSiteSignature(
        Type returnType,
        Type[] parameters,
        SignatureCallingConvention convention,
        bool hasThis,
        bool explicitThis) {
        ReturnType = returnType;
        Parameters = parameters;
        Convention = convention;
        HasThis = hasThis;
        ExplicitThis = explicitThis;
    }

    internal Type ReturnType { get; }

    internal Type[] Parameters { get; }

    internal SignatureCallingConvention Convention { get; }

    internal bool HasThis { get; }

    internal bool ExplicitThis { get; }

    internal int StackPops => Parameters.Length + (HasThis ? 1 : 0) + 1;

    internal bool ReturnsVoid => ReturnType == typeof(void);
}
