using System.Runtime.CompilerServices;

[assembly: Concord.PatchRegistry(typeof(Concord.Generators.Tests.Consumer.ConsumerRegistry))]

namespace Concord.Generators.Tests.Consumer;

/// <summary>The provider the assembly's [PatchRegistry] points at.</summary>
public sealed class ConsumerRegistry : IPatchDeclarationProvider {
    public static readonly Type[] All = [
        typeof(SecretCounterPatch),
        typeof(SurfaceCapturePatch),
        typeof(SurfaceNewPatch),
        typeof(SurfaceSlicePatch),
        typeof(SurfaceStatePatch),
        typeof(FirstQueuePatch),
        typeof(SecondQueuePatch),
        typeof(DebuggedQueuePatch),
        typeof(DepositFieldPatch),
        typeof(FeePropertyPatch),
        typeof(SettleMethodPatch),
        typeof(OwnerInstancePatch),
        typeof(VisitAttachedPatch),
        typeof(LocalIndexPatch),
        typeof(LocalOrdinalPatch),
        typeof(BoundPatch),
        typeof(ConsumerWeathers),
    ];

    /// <inheritdoc />
    public IReadOnlyList<Type> Declarations => All;
}

/// <summary>The target of the [PatchBefore], [PatchAfter] and [PatchDebug] fixtures.</summary>
public class DispatchQueue {
    /// <summary>Each injection and target body appends its name here as it runs.</summary>
    public static readonly List<string> Runs = [];

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Ordered() {
        Runs.Add("target");
    }

    /// <summary>Patched by <see cref="DebuggedQueuePatch" />.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Debugged() {
        Runs.Add("debugged");
    }
}

[Patch]
[PatchBefore(typeof(SecondQueuePatch))]
[PatchBefore("Optional.Mod.Before")]
public abstract class FirstQueuePatch : DispatchQueue {
    [Inject(At.Head, nameof(Ordered))]
    private void OnOrdered() {
        Runs.Add("first");
    }
}

[Patch]
[PatchAfter(typeof(FirstQueuePatch))]
[PatchAfter("Optional.Mod.After")]
public abstract class SecondQueuePatch : DispatchQueue {
    [Inject(At.Head, nameof(Ordered))]
    private void OnOrdered() {
        Runs.Add("second");
    }
}

/// <summary>Proves [PatchDebug] marks the injections of its own declaration.</summary>
[Patch]
[PatchDebug]
public abstract class DebuggedQueuePatch : DispatchQueue {
    [Inject(At.Head, nameof(Debugged))]
    private void OnDebugged() {
        Runs.Add("debug");
    }
}
