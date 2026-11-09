using System.Reflection;

namespace Concord.Emit;

/// <summary>
///     Opens a method a composed body can be written into, on a runtime where Concord cannot emit
///     one itself. A backend installs its generator through <see cref="ScratchGenerator.Current" />.
/// </summary>
public interface IScratchGenerator {
    /// <summary>Opens a scratch body Concord writes a composed body into.</summary>
    /// <param name="name">The name to give the emitted method.</param>
    /// <param name="returnType">The emitted method's return type.</param>
    /// <param name="parameterTypes">The emitted method's parameter types, in order.</param>
    /// <param name="origin">The method the body was composed from, used in refusal messages.</param>
    /// <returns>A sink that accepts the body and hands back the method that runs it.</returns>
    IScratchBody Begin(string name, Type returnType, Type[] parameterTypes, MethodBase origin);
}
