using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace Concord.MonoMod.Harmony;

internal sealed class HarmonyLockScope : IDisposable
{
    private static readonly object? Locker = LockerOf(typeof(HarmonyLib.PatchProcessor).Assembly);

    private static readonly Dictionary<Assembly, object?> Lockers = new Dictionary<Assembly, object?>();

    private readonly object entered;

    private HarmonyLockScope(object locker)
    {
        entered = locker;
    }

    internal static bool Available { get; } = Locker != null;

    internal static bool Held => Locker != null && Monitor.IsEntered(Locker);

    public void Dispose()
    {
        Monitor.Exit(entered);
    }

    internal static IDisposable Enter()
    {
        return EnterOn(Locker!);
    }

    internal static IDisposable? For(Assembly copy)
    {
        object? locker;
        lock (Lockers)
        {
            if (!Lockers.TryGetValue(copy, out locker))
            {
                locker = LockerOf(copy);
                Lockers[copy] = locker;
            }
        }

        return locker == null ? null : EnterOn(locker);
    }

    private static IDisposable EnterOn(object locker)
    {
        Monitor.Enter(locker);
        return new HarmonyLockScope(locker);
    }

    private static object? LockerOf(Assembly copy)
    {
        FieldInfo? field = copy.GetType("HarmonyLib.PatchProcessor")?
            .GetField("locker", BindingFlags.NonPublic | BindingFlags.Static);

        return field?.GetValue(null);
    }
}
