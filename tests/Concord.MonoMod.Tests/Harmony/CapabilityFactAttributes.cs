using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.MonoMod.Harmony;
using HarmonyLib;
using Xunit;

namespace Concord.MonoMod.Tests;

public sealed class InnerPatchFactAttribute : FactAttribute
{
    private static readonly string? Refusal = Probe();

    public InnerPatchFactAttribute()
    {
        if (Refusal != null)
        {
            Skip = Refusal;
        }
    }

    private static string? Probe()
    {
        Assembly harmony = typeof(HarmonyLib.Harmony).Assembly;
        if (harmony.GetType("HarmonyLib.InnerMethod") != null)
        {
            return null;
        }

        return "the Harmony this run loaded carries no HarmonyLib.InnerMethod, so no inner patch can be registered through it and the fact body cannot resolve the members it reads";
    }
}

public sealed class ExceptionFilterPatchFactAttribute : FactAttribute
{
    private static readonly string? Refusal = Probe();

    public ExceptionFilterPatchFactAttribute()
    {
        if (Refusal != null)
        {
            Skip = Refusal;
        }
    }

    private static string? Probe()
    {
        MethodInfo target = typeof(ExceptionFilterPatchFactAttribute)
            .GetMethod(nameof(FilterBody), BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo noop = typeof(ExceptionFilterPatchFactAttribute)
            .GetMethod(nameof(Noop), BindingFlags.NonPublic | BindingFlags.Static)!;

        try
        {
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("concord.probe.exceptionfilter");
            harmony.Patch(target, prefix: new HarmonyMethod(noop));
            harmony.Unpatch(target, noop);
            return null;
        }
        catch (Exception ex)
        {
            return "the Harmony this run loaded cannot patch a method carrying an exception filter on this runtime, with no Concord in the way: " +
                   ex.GetType().Name + ": " + ex.Message;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int FilterBody(int code)
    {
        try
        {
            if (code != 0)
            {
                throw new InvalidOperationException(code.ToString());
            }

            return 0;
        }
        catch (InvalidOperationException ex) when (ex.Message == "1")
        {
            return 1;
        }
    }

    private static void Noop()
    {
    }
}
