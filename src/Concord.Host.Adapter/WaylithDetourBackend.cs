using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Detour;

namespace Concord.Host.Adapter;

/// <summary>
///     One row of the map the runtime uses to turn an offset in the written body back into an offset in the
///     target's own IL, which is what a stack trace line number is resolved against.
/// </summary>
public readonly struct OffsetPair {
    /// <summary>Initializes a new instance.</summary>
    /// <param name="original">The offset in the target's own IL.</param>
    /// <param name="replaced">The offset in the body that was written over it.</param>
    public OffsetPair(int original, int replaced) {
        Original = original;
        Replaced = replaced;
    }

    /// <summary>The offset in the target's own IL.</summary>
    public int Original { get; }

    /// <summary>The offset in the written body that maps back to <see cref="Original" />.</summary>
    public int Replaced { get; }
}

/// <summary>
///     The <see cref="IDetourBackend" /> a host installs to get body replacement through the runtime's
///     transformer pipeline. <see cref="ApplyComposed" /> is the way in.
/// </summary>
public sealed class WaylithDetourBackend : IDetourBackend, IOwnsBodyQuery, IRecomposeTarget {
    /// <summary>Log prefix for a target published without an offset map, so its line numbers are unreliable.</summary>
    public const string NoOffsetMapMarker = "[Concord.Waylith] no-offset-map";

    /// <summary>The highest original offset the runtime is known to carry; past it no pairing is safe.</summary>
    public const int SentinelCeiling = 100_000_000;

    private static bool emptyMapAlways;

    private readonly ITargetBodyOwner publisher;

    private readonly WaylithTargetStack stack;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="publisher">The owner of each staged target's body, normally a <see cref="StagePublisher" />.</param>
    /// <exception cref="ArgumentNullException"><paramref name="publisher" /> is <see langword="null" />.</exception>
    public WaylithDetourBackend(ITargetBodyOwner publisher) {
        ArgumentNullException.ThrowIfNull(publisher);
        this.publisher = publisher;
        WrapperComposer.CaptureComposedBody = true;
        stack = new WaylithTargetStack(publisher, Log);
        ReversePatchFactory.OwnsBody = OwnsBody;
    }

    /// <summary>Where this backend's reports go. It writes to <see cref="Trace" /> until a host redirects it.</summary>
    public static Action<string> Log { get; set; } = static message => Trace.WriteLine(message);

    /// <summary>
    ///     When on, no offset map is written for any body, so stack traces carry raw offsets. The environment
    ///     can turn it on too, so reading <see langword="false" /> back is not guaranteed.
    /// </summary>
    public static bool EmptyMapAlways {
        get => emptyMapAlways || AdapterEntryPoint.EmptyMapRequested(Log);
        set => emptyMapAlways = value;
    }

    /// <inheritdoc />
    public int PatchedTargetCount => publisher.PublishedTargetCount;

    /// <summary>Pairs each instruction's offset in the written body with an offset in the target's own IL.</summary>
    /// <param name="target">The method the body was composed for.</param>
    /// <param name="body">The composed body, whose instructions carry their origin offsets.</param>
    /// <param name="encoded">The same body encoded, whose offsets must line up one for one.</param>
    /// <returns>The map, or an empty list when no pairing is safe and raw offsets get reported instead.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">The composed and encoded bodies disagree on instruction count.</exception>
    public static IReadOnlyList<OffsetPair> OffsetMap(MethodBase target, ComposedBody body, EncodedBody encoded) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(encoded);

        return OffsetMap(body, encoded, Band(target, encoded));
    }

    /// <summary>The offset a composed instruction with no origin is mapped to, laid after the target's own IL.</summary>
    /// <param name="target">The method whose original IL length sets the band.</param>
    /// <param name="encoded">The encoded body, whose size has to fit under the ceiling.</param>
    /// <param name="inputIlLength">The length of the IL the body was composed over. <c>-1</c> reads the target's declared body instead, which is only the same length when nothing reshaped the body before Concord saw it.</param>
    /// <returns><c>-1</c> when the target has no readable body or the band would pass <see cref="SentinelCeiling" />.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null" />.</exception>
    public static int SentinelBand(MethodBase target, EncodedBody encoded, int inputIlLength = -1) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(encoded);

        int original = inputIlLength >= 0
            ? inputIlLength
            : ForkBodyReader.Shared.Read(target)?.Il.Length
                ?? target.GetMethodBody()?.GetILAsByteArray()?.Length
                ?? -1;
        return original < 0 || (long)original + encoded.CodeSize > SentinelCeiling ? -1 : original;
    }

    /// <summary>Lists the mods behind a set of injections, for a message that reports attribution.</summary>
    /// <param name="ordered">The injections to read owners off, in the order they were composed.</param>
    /// <returns>Each distinct owner name once, compared byte for byte, in the order it first appears.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ordered" /> is <see langword="null" />.</exception>
    public static IReadOnlyList<string> DistinctOwners(IReadOnlyList<Injection> ordered) {
        ArgumentNullException.ThrowIfNull(ordered);

        List<string> owners = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Injection injection in ordered) {
            if (seen.Add(injection.Owner)) {
                owners.Add(injection.Owner);
            }
        }

        return owners;
    }

    /// <summary>Builds the warning logged when a target is published with no offset map.</summary>
    /// <param name="target">The method that was published.</param>
    /// <param name="owners">How many mods own injections in it; <c>0</c> drops the attribution sentence.</param>
    /// <returns>A message starting with <see cref="NoOffsetMapMarker" />, naming the cause and the cost.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <see langword="null" />.</exception>
    public static string NoOffsetMapWarning(MethodBase target, int owners) {
        ArgumentNullException.ThrowIfNull(target);

        string cause = EmptyMapAlways
            ? $"{AdapterEntryPoint.EmptyMapVariable} is on, so no map is written for any body, whatever " +
                "its owners. Turn it off to get sentinel maps back"
            : "the body written for it, laid end to end after the target's own il, runs past the offset the " +
                $"runtime is known to carry ({SentinelCeiling}), so no pairing was safe";

        string attribution = owners > 0
            ? " The mod named as the owner of a frame in it stays correct."
            : string.Empty;

        return NoOffsetMapMarker + $" '{Describe(target)}': {cause}. The runtime reports raw offsets into the " +
            "written body instead, and a pdb resolves those against the original body, so any line number " +
            "reported for this method is unreliable: it names a real line that is not the code that ran." +
            attribution;
    }

    /// <inheritdoc />
    /// <remarks>True for a target this backend has a live staged body on.</remarks>
    public bool OwnsBody(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        return publisher.IsPublished(BodyScope.KeyOf(target));
    }

    /// <inheritdoc />
    /// <remarks>Rebuilds the target through the pipeline, which re-reads its default body on the way in.</remarks>
    public bool Recompose(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        return publisher.Rebuild(BodyScope.KeyOf(target));
    }

    /// <inheritdoc />
    public IReadOnlyList<string> OwnersOf(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        return stack.OwnersOf(target);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     A target no body can be staged over is refused here, since no second backend is installed, and the
    ///     refusal is logged once per target before it is raised. A staging that fails later reports through
    ///     <see cref="IDetourHandle.IsApplied" /> and one log line, not through an exception out of this call.
    ///     A generic target is keyed by body scope, so one body serves every instantiation.
    /// </remarks>
    /// <exception cref="ConcordEmitException">This target cannot carry a staged body.</exception>
    public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(added);

        MethodBase key = BodyScope.KeyOf(target);
        TargetVerdict verdict = TargetEligibility.Judge(key);
        if (verdict.Handling == TargetHandling.Replace) {
            verdict = SharedGenericPolicy.Judge(target);
        }

        if (verdict.Handling != TargetHandling.Replace) {
            if (TargetEligibility.ReportOnce(key)) {
                Log(verdict.Message);
            }

            verdict.ThrowIfRefused();
        }

        return stack.Add(target, added);
    }

    internal static IReadOnlyList<OffsetPair> OffsetMap(ComposedBody body, EncodedBody encoded, int band) {
        RequirePairable(body, encoded, "an offset map");

        if (band < 0) {
            return [];
        }

        List<OffsetPair> pairs = new List<OffsetPair>(body.Instructions.Count);
        for (int i = 0; i < body.Instructions.Count; i++) {
            int origin = body.Instructions[i].OriginOffset ?? (band + encoded.InstructionOffsets[i]);
            if (pairs.Count > 0 && pairs[^1].Original == origin) {
                continue;
            }

            pairs.Add(new OffsetPair(origin, encoded.InstructionOffsets[i]));
        }

        return pairs;
    }

    internal static void RequirePairable(ComposedBody body, EncodedBody encoded, string use) {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(encoded);

        if (body.Instructions.Count != encoded.InstructionOffsets.Count) {
            throw new ArgumentException(
                $"The composed body holds {body.Instructions.Count} instructions and the encoded body reports " +
                $"{encoded.InstructionOffsets.Count} offsets, so the two cannot be paired into {use}.",
                nameof(encoded));
        }
    }

    internal static int Band(MethodBase target, EncodedBody encoded, int inputIlLength = -1) {
        return EmptyMapAlways || target is DynamicMethod ? -1 : SentinelBand(target, encoded, inputIlLength);
    }

    internal static string Describe(MethodBase method) {
        return (method.DeclaringType?.FullName ?? method.Module.Name) + "." + method.Name;
    }
}
