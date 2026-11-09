using System;
using System.Collections.Generic;
using System.Reflection;
using Concord.Backend;
using Concord.Emit;
using Concord.MonoMod;
using Concord.MonoMod.Harmony;
using HarmonyLib;

namespace Concord.MonoMod.Tests;

internal enum RouteKind {
    NotContested,
    Routed,
}

internal sealed class RouteOutcome {
    internal RouteOutcome(RouteKind kind, IBackendHandle? handle) {
        Kind = kind;
        Handle = handle;
    }

    internal RouteKind Kind { get; }

    internal IBackendHandle? Handle { get; }
}

internal sealed class CoexRouter : IDisposable {
    private const string PinId = "test.coex.pin";

    private static readonly MethodInfo PinPrefix =
        typeof(CoexRouter).GetMethod(nameof(Pinned), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly HarmonyLib.Harmony pin = new HarmonyLib.Harmony(PinId);
    private readonly List<MethodBase> pinned = new List<MethodBase>();

    internal CoexRouter(Action<string> log) {
        Router = new RoutingBackend(new MonoModBackend(), log);
        Router.ActivateHost(new HarmonyBridge(log));
    }

    internal RoutingBackend Router { get; }

    public void Dispose() {
        TestUnpatch.Own(pin, PinId);
        pinned.Clear();
        UpdateWrapperHook.DetachObserver();
    }

    internal RouteOutcome Route(MethodBase target, IReadOnlyList<Injection> added, bool harmonyOwns) {
        if (harmonyOwns) {
            pin.Patch((MethodBase)target, prefix: new HarmonyMethod(PinPrefix));
            pinned.Add(target);
        }

        IBackendHandle handle = Router.ApplyComposed(target, added);
        return new RouteOutcome(Router.GetRoute(target) == RouteState.HarmonyOwned ? RouteKind.Routed : RouteKind.NotContested, handle);
    }

    internal IBackendHandle Apply(MethodBase target, IReadOnlyList<Injection> added) {
        return Router.ApplyComposed(target, added);
    }

    internal RouteState RouteOf(MethodBase target) {
        return Router.GetRoute(target);
    }

    private static void Pinned() {
    }
}
