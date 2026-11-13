using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Backend;

namespace Concord.Waylith;

/// <summary>What an environment variable's raw text asked for, once read.</summary>
public enum SwitchState {
    /// <summary>The variable is missing or blank, so it asks for nothing.</summary>
    Unset,

    /// <summary>The text reads as on, one of <c>1</c>, <c>true</c>, <c>on</c>, <c>yes</c> or <c>enabled</c>.</summary>
    On,

    /// <summary>The text reads as off, one of <c>0</c>, <c>false</c>, <c>off</c>, <c>no</c> or <c>disabled</c>.</summary>
    Off,

    /// <summary>The text is neither on nor off. It is logged once and then treated as unset.</summary>
    Unreadable,
}

/// <summary>
///     What a host mod calls to install the body-replacement backend. Call it once, before
///     the first patch applies: a swap after that would strand every patched target on the old backend.
/// </summary>
public static class AdapterEntryPoint {
    /// <summary>Environment variable that drops the offset map from every written body, so frames report raw offsets.</summary>
    public const string EmptyMapVariable = "CONCORD_WAYLITH_EMPTY_MAP";

    internal const string DeclaredBodyRefusalCode = "CONC178";

    internal static readonly Func<bool> ConcordDeclaredBodyReads = ProbeDeclaredBodyReader;

    private static readonly string[] On = ["1", "true", "on", "yes", "enabled"];

    private static readonly string[] Off = ["0", "false", "off", "no", "disabled"];

    private static readonly object Gate = new object();

    private static readonly Func<int> ConcordPatchedTargets = static () => Patcher.PatchedTargetCount;

    [ThreadStatic]
    private static StageInputSlot? transformInput;

    private static string? warnedEmptyMap;

    /// <summary>The choice a completed install made, or <c>null</c> before the first install. A second install returns this one unchanged.</summary>
    public static Selection? Current { get; private set; }

    /// <summary>Overrides the count of already-patched targets the install checks before swapping. Left <c>null</c>, Concord's own count is used.</summary>
    public static Func<int>? PatchedTargets { get; set; }

    /// <summary>Overrides whether a runtime that replaces bodies will also read a declared one back. Left <c>null</c>, the real bind result is used.</summary>
    public static Func<bool>? DeclaredBodyReads { get; set; }

    internal static Func<MethodBase, RuntimeMethodHandle>? DefinitionHandle { get; set; }

    /// <summary>Installs the backend, probing this runtime for body-replacement support.</summary>
    /// <param name="log">Where to write the one install line. <c>null</c> sends it to <see cref="Trace" />.</param>
    /// <param name="assemblyDirectories">Directories to find a byte-loaded mod assembly's file in. Empty or <c>null</c> logs a warning and leaves [Local(Name)] unresolvable.</param>
    /// <returns>The outcome. Inspect <see cref="Selection.Waylith" /> to see whether body replacement is live.</returns>
    public static Selection Install(Action<string>? log, string[]? assemblyDirectories) {
        return Install(log, assemblyDirectories, RuntimeCapability.Support, null);
    }

    /// <summary>Installs the backend with the capability verdict and the backend factory supplied, for tests and unusual hosts.</summary>
    /// <param name="log">Where to write the one install line. <c>null</c> sends it to <see cref="Trace" />.</param>
    /// <param name="assemblyDirectories">Directories to find a byte-loaded mod assembly's file in.</param>
    /// <param name="support">The capability verdict to trust instead of probing the runtime.</param>
    /// <param name="backend">Builds the backend to install, or <c>null</c> for the standard one, which needs a registered body publisher.</param>
    /// <returns>The outcome, which installs nothing when a target is already patched or no publisher is registered.</returns>
    public static Selection Install(
        Action<string>? log,
        string[]? assemblyDirectories,
        BodyReplacement support,
        Func<IPatchBackend>? backend) {
        lock (Gate) {
            if (Current is { } already) {
                return already;
            }

            Action<string> write = log ?? (message => Trace.WriteLine(message));
            RegisterLocalNameResolver(assemblyDirectories, write);

            Selection chosen = Choose(support);
            if (chosen.Waylith) {
                chosen = Swap(chosen, backend, write);
            }

            write(chosen.Message);
            Current = chosen;
            return chosen;
        }
    }

    /// <summary>Points Concord's original-body reads at the host, when this runtime offers one.</summary>
    /// <returns><see langword="true" /> when the host read bound and is now in use.</returns>
    public static bool RegisterDeclaredBodyReader() {
        if (!ForkBodyReader.Shared.IsAvailable) {
            return false;
        }

        OriginalBody.HostOriginalBody = DeclaredBody;
        OriginalBody.HostMethodBody = DeclaredMethodBodyFor;
        return true;
    }

    /// <summary>Reads one environment variable's text, case-insensitively and ignoring surrounding space.</summary>
    /// <param name="raw">The variable's value, or <c>null</c> when it is not set.</param>
    /// <returns>Unset for null or blank, On or Off for a word it knows, Unreadable for anything else.</returns>
    public static SwitchState ReadSwitch(string? raw) {
        if (raw is null) {
            return SwitchState.Unset;
        }

        string trimmed = raw.Trim();
        if (trimmed.Length == 0) {
            return SwitchState.Unset;
        }

        if (Matches(On, trimmed)) {
            return SwitchState.On;
        }

        return Matches(Off, trimmed) ? SwitchState.Off : SwitchState.Unreadable;
    }

    /// <summary>Whether <see cref="EmptyMapVariable" /> asks for bodies to be written with no offset map.</summary>
    /// <param name="log">Where to warn about text that reads as neither on nor off. <c>null</c> sends it to <see cref="Trace" />.</param>
    /// <returns><c>true</c> only when the variable reads as on. Text it cannot read warns once and returns <c>false</c>.</returns>
    public static bool EmptyMapRequested(Action<string>? log) {
        string raw = Environment.GetEnvironmentVariable(EmptyMapVariable) ?? string.Empty;
        SwitchState requested = ReadSwitch(raw);

        if (requested != SwitchState.Unreadable) {
            return requested == SwitchState.On;
        }

        if (!string.Equals(warnedEmptyMap, raw, StringComparison.Ordinal)) {
            warnedEmptyMap = raw;
            (log ?? (message => Trace.WriteLine(message)))(
                Selection.Prefix + EmptyMapVariable + " is set to '" + raw + "', which is neither " +
                string.Join("/", On) + " nor " + string.Join("/", Off) + ". Ignoring it, so every body keeps " +
                "the sentinel offset map that tells injected code apart from the target's own il.");
        }

        return false;
    }

    /// <summary>Builds the lookup that turns a module into the file on disk it was loaded from, searching the directories in order.</summary>
    /// <param name="directories">Directories to search. Copied, so a later change to the array is not picked up.</param>
    /// <returns>A lookup returning the first path that exists, or <c>null</c> when the module's file is in none of them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="directories" /> is <c>null</c>.</exception>
    public static Func<Module, string?> LocalNameResolver(string[] directories) {
        ArgumentNullException.ThrowIfNull(directories);
        string[] copy = (string[])directories.Clone();

        return module => {
            foreach (string directory in copy) {
                string candidate = Path.Combine(directory, module.ScopeName);
                if (File.Exists(candidate)) {
                    return candidate;
                }
            }

            return null;
        };
    }

    /// <summary>Forgets the installed selection. For tests, never for a running game.</summary>
    public static void ResetForTests() {
        lock (Gate) {
            Current = null;
            warnedEmptyMap = null;
        }
    }

    /// <summary>Judges a capability verdict, installing nothing.</summary>
    /// <param name="support">What this runtime can do, as probed.</param>
    /// <returns>Body replacement when the verdict stages a composed body, else a refusal carrying the verdict's explanation.</returns>
    public static Selection Choose(BodyReplacement support) {
        return new Selection(
            RuntimeCapability.Stages(support),
            RuntimeCapability.Explain(support, RuntimeCapability.ProbeFailure) + ".");
    }

    internal static StageInputSlot? StageInput(RuntimeMethodHandle method, ComposedBody input) {
        StageInputSlot? outer = transformInput;
        transformInput = new StageInputSlot(method, input);
        return outer;
    }

    internal static void RestoreStageInput(StageInputSlot? outer) {
        transformInput = outer;
    }

    private static Selection Swap(Selection chosen, Func<IPatchBackend>? backend, Action<string> write) {
        int patched = (PatchedTargets ?? ConcordPatchedTargets)();
        if (patched != 0) {
            return new Selection(
                false,
                patched + " target(s) already carry a composed body, and a swap now strands every one of them on " +
                "the backend installed before this one with nothing recording that it happened. Install before the " +
                "first patch applies.");
        }

        if (!(DeclaredBodyReads ?? ConcordDeclaredBodyReads)()) {
            return new Selection(
                false,
                "this runtime replaces method bodies but will not read a declared one back, so every read of a " +
                "target's original body would return whatever body Concord last staged over it.");
        }

        write(RuntimeCapability.SettleAccessChecks(RuntimeCapability.HasPipeline ? AccessCheckProbe.Run : null));
        WaylithClrBackend.Log = write;
        PatchBackend.Current = backend is null ? StagedBackend() : backend();
        return chosen;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IPatchBackend StagedBackend() {
        return new WaylithClrBackend(ConcordStage.Install());
    }

    private static ComposedBody? DeclaredBody(MethodBase method) {
        if (transformInput is { } staged && DefinitionHandle is { } resolve && resolve(method).Equals(staged.Method)) {
            return staged.Input;
        }

        HostBody? read = ForkBodyReader.Shared.Read(method);
        if (read is null) {
            throw Refuse(method, "this host would not read its declared body back");
        }

        ComposedBody? decoded = BodyDecoder.Decode(method, read, out string? refusal);
        return decoded ?? throw Refuse(method, "Concord could not decode the body this host read back: " + refusal);
    }

    private static System.Reflection.MethodBody? DeclaredMethodBodyFor(MethodBase method) {
        HostBody? read = ForkBodyReader.Shared.Read(method);
        if (read is null) {
            throw Refuse(method, "this host would not read its declared body back");
        }

        ComposedLocal[]? locals = LocalSignatureDecoder.Decode(
            read.LocalSignature.ToArray(), method, out string? refusal);
        if (locals is null) {
            throw Refuse(method, "Concord could not decode the local signature this host read back: " + refusal);
        }

        return DeclaredMethodBody.Build(method, read, locals);
    }

    private static ConcordEmitException Refuse(MethodBase method, string because) {
        return new ConcordEmitException(
            DeclaredBodyRefusalCode,
            $"'{WaylithClrBackend.Describe(method)}' cannot be read as it was " +
            $"declared: {because}. This runtime reports a replaced body through reflection, so falling back to " +
            "that read would hand out a patched body as though it were the original, and Concord refuses the " +
            "method instead.");
    }

    private static void RegisterLocalNameResolver(string[]? assemblyDirectories, Action<string> write) {
        if (assemblyDirectories is null || assemblyDirectories.Length == 0) {
            write(
                Selection.Prefix + "no assembly directories were supplied, so a mod assembly this host byte-loads " +
                "has no file on disk for Concord to read symbols or a method body from. [Local(Name = ...)] on a " +
                "target in one reports CONC151, and any check that needs the file cannot run.");
            return;
        }

        Patcher.UseLocalNameResolver(LocalNameResolver(assemblyDirectories));
    }

    private static bool Matches(string[] known, string value) {
        foreach (string candidate in known) {
            if (string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static bool ProbeDeclaredBodyReader() {
        if (!RegisterDeclaredBodyReader()) {
            return false;
        }

        MethodInfo probe = typeof(AdapterEntryPoint).GetMethod(nameof(ReadSwitch))!;
        return ForkBodyReader.Shared.Read(probe) is not null;
    }
}

/// <summary>Which backend <see cref="AdapterEntryPoint.Install(System.Action{string},string[])" /> settled on, and why.</summary>
public sealed class Selection {
    internal const string Prefix = "[Concord.Waylith] ";

    /// <summary>Initializes a new instance.</summary>
    /// <param name="waylith">Whether body replacement was chosen.</param>
    /// <param name="reason">The sentence explaining the choice, ending in a period.</param>
    public Selection(bool waylith, string reason) {
        Waylith = waylith;
        Reason = reason;
    }

    /// <summary>Whether bodies get replaced in place. When <c>false</c>, no backend is installed at all.</summary>
    public bool Waylith { get; }

    /// <summary>The one name a host reads off whichever backend it installed, to see whether a backend is live.</summary>
    public bool Installed => Waylith;

    /// <summary>Why body replacement was picked, or why this runtime could not run it.</summary>
    public string Reason { get; }

    /// <summary>The single line a host logs. Body replacement names only this runtime; a refusal carries <see cref="Reason" />.</summary>
    public string Message => Prefix +
        (Waylith
            ? "backend: body replacement on '" + RuntimeCapability.Runtime + "'."
            : "no backend installed, so nothing Concord is asked to patch will be patched. " + Reason);
}

internal sealed class StageInputSlot {
    internal StageInputSlot(RuntimeMethodHandle method, ComposedBody input) {
        Method = method;
        Input = input;
    }

    internal RuntimeMethodHandle Method { get; }

    internal ComposedBody Input { get; }
}
