using System.Reflection;
using Concord.Backend;

namespace Concord.Waylith;

/// <summary>
///     Refuses body replacement on a generic that shares one IL body with its siblings. Replacing that body would
///     change every instantiation compiled from it, including ones no patch asked for.
/// </summary>
public static class SharedGenericPolicy {
    /// <summary>The code carried by the refusal for a generic definition with no instantiation named.</summary>
    public const string OpenDefinitionCode = "CONC061";

    /// <summary>Decides how <paramref name="target" /> has to be patched, given what it shares its IL body with.</summary>
    /// <param name="target">The method a patch wants to apply to.</param>
    /// <returns>Accept when the body is this method's alone, Refuse for a closed shared-body instantiation and for an open definition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <c>null</c>.</exception>
    public static TargetVerdict Judge(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);

        if (!SharesOneBody(target)) {
            return TargetVerdict.Accept();
        }

        string slot = $"'{WaylithClrBackend.Describe(TypicalDefinition(target))}'";
        if (target.ContainsGenericParameters) {
            return TargetVerdict.Refuse(
                OpenDefinitionCode,
                $"{TargetEligibility.RefusalMarker} {slot} was requested as a generic definition, so no " +
                "instantiation was named. The runtime keeps " +
                "one IL body for the definition and every instantiation compiled from it, so replacing that body " +
                "changes all of them. Ask for a closed instantiation instead.");
        }

        return TargetVerdict.Refuse(
            StagePublisher.RefusedCode,
            $"{TargetEligibility.RefusalMarker} '{WaylithClrBackend.Describe(target)}' shares one IL body with " +
            $"every other instantiation of {slot}, value-type ones included. Replacing that body would change " +
            "all of them, including instantiations no patch asked for, so every patch on this target is " +
            "refused and the method keeps the body it was declared with.");
    }

    /// <summary>Whether <paramref name="target" />'s IL body is shared with other instantiations rather than its own.</summary>
    /// <param name="target">The method to test.</param>
    /// <returns><c>true</c> for a generic method, a method on a generic type, or anything still carrying type parameters.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <c>null</c>.</exception>
    public static bool SharesOneBody(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);

        return target.ContainsGenericParameters ||
            target is MethodInfo { IsGenericMethod: true } ||
            target.DeclaringType?.IsGenericType == true;
    }

    /// <summary>The one method that owns the IL body <paramref name="target" /> runs, which every sibling shares.</summary>
    /// <param name="target">The method to walk back to its definition.</param>
    /// <returns>The generic method definition on the generic type definition, or <paramref name="target" /> itself when nothing is shared.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <c>null</c>.</exception>
    public static MethodBase TypicalDefinition(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);

        MethodBase definition = target is MethodInfo { IsGenericMethod: true, IsGenericMethodDefinition: false } generic
            ? generic.GetGenericMethodDefinition()
            : target;

        if (definition.DeclaringType is not { IsConstructedGenericType: true } || definition.Module.Assembly.IsDynamic) {
            return definition;
        }

        return definition.Module.ResolveMethod(definition.MetadataToken) ?? definition;
    }
}
