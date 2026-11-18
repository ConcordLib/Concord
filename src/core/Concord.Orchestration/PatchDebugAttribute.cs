namespace Concord;

/// <summary>
///     Marks a patch declaration to append its composed wrapper IL to <c>Concord.PatchDebug.log</c>
///     on the current user's desktop when Concord applies it.
/// </summary>
[System.Diagnostics.Conditional("CONCORD_ATTRIBUTES_IN_METADATA")]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PatchDebugAttribute : Attribute { }
