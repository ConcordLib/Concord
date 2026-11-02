using System.Reflection;
using MonoMod.Utils;

namespace Concord.Emit;

/// <summary>
///     Creates callable clones of original target method bodies.
/// </summary>
public static class OriginalBody {
    /// <summary>Reads a method's declared body from the host, for runtimes where reflection reports the live one.</summary>
    public static Func<MethodBase, ComposedBody?>? HostOriginalBody { get; set; }

    /// <summary>Reads a method's declared body in reflection's own shape, for a consumer that reads IL itself.</summary>
    public static Func<MethodBase, System.Reflection.MethodBody?>? HostMethodBody { get; set; }

    /// <summary>
    ///     The method as declared: itself where reflection is honest, else a view whose
    ///     <see cref="MethodBase.GetMethodBody" /> is the body the host declares.
    /// </summary>
    /// <param name="original">The method to view.</param>
    /// <returns><paramref name="original" /> unchanged when no host read is registered.</returns>
    public static MethodBase AsDeclared(MethodBase original) {
        Func<MethodBase, System.Reflection.MethodBody?>? read = HostMethodBody;
        if (read is null || original is DeclaredView) {
            return original;
        }

        return new DeclaredView(original, read(original));
    }

    /// <summary>
    ///     Clones an original method into an unmanaged dynamic method that is not affected by Concord detours.
    /// </summary>
    /// <param name="original">The method body to clone.</param>
    /// <returns>
    ///     A callable method with the same effective signature. Instance targets receive the instance as
    ///     the first delegate argument.
    /// </returns>
    public static MethodInfo Clone(MethodBase original) {
        using DynamicMethodDefinition dmd = BodyReader.OpenOriginal(original);
        return dmd.Generate();
    }
}
