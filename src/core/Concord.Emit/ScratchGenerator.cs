namespace Concord.Emit;

/// <summary>
///     The scratch generator Concord emits composed bodies through on a runtime that has no
///     emit route of its own. A backend sets this while it installs.
/// </summary>
public static class ScratchGenerator {
    /// <summary>
    ///     The installed generator, or <see langword="null" /> when no backend installed one.
    ///     Runtimes with their own emit route ignore this.
    /// </summary>
    public static IScratchGenerator? Current { get; set; }
}
