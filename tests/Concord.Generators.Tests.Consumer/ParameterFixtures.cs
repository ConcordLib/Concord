using System.Globalization;
using System.Runtime.CompilerServices;

namespace Concord.Generators.Tests.Consumer;

/// <summary>The target of the [Local] and [Bound] fixtures.</summary>
public class TallyBoard {
    public static int Recorded;

    /// <summary>Patched by <see cref="LocalIndexPatch" /> and <see cref="LocalOrdinalPatch" />.</summary>
    /// <param name="seed">Slot 0 holds twice this, slot 1 three times this.</param>
    /// <returns>Both locals as text.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public string TwoLocals(int seed) {
        int doubled = seed * 2;
        int tripled = seed * 3;
        return string.Concat(doubled.ToString(CultureInfo.InvariantCulture), tripled.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Patched by <see cref="BoundPatch" />.</summary>
    /// <param name="sectionId">The argument the bound value replaces in the injection.</param>
    /// <returns>Twice <paramref name="sectionId" />.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Plain(int sectionId) {
        return sectionId * 2;
    }
}

[Patch]
public abstract class LocalIndexPatch : TallyBoard {
    [Inject(At.Return, nameof(TwoLocals))]
    private void ReadByIndex([Local(Index = 1)] int value) {
        Recorded = value;
    }
}

[Patch]
public abstract class LocalOrdinalPatch : TallyBoard {
    [Inject(At.Return, nameof(TwoLocals))]
    private void ReadByOrdinal([Local(Ordinal = 1)] int value) {
        Recorded = value;
    }
}

[Patch]
public abstract class BoundPatch : TallyBoard {
    [Inject(At.Head, nameof(Plain))]
    private void OnPlain([Bound] int sectionId) {
        Recorded = sectionId;
    }
}
