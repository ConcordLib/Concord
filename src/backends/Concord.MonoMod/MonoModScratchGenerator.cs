using System.Reflection;
using Concord.Emit;
using MonoMod.Utils;

namespace Concord.MonoMod;

/// <summary>
///     Emits a composed body through MonoMod's <c>DynamicMethodDefinition</c>, the emit route for
///     runtimes with no <c>DynamicILInfo</c> of their own.
/// </summary>
public sealed class MonoModScratchGenerator : IScratchGenerator {
    /// <inheritdoc />
    public IScratchBody Begin(string name, Type returnType, Type[] parameterTypes, MethodBase origin) {
        return new MonoModScratchBody(new DynamicMethodDefinition(name, returnType, parameterTypes), origin);
    }
}
