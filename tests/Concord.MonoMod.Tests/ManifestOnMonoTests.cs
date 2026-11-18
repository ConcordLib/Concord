using System;
using System.Collections.Generic;
using System.Reflection;
using Concord.Backend;
using Concord.MonoMod.Tests.ManifestAssembly;
using Xunit;

namespace Concord.MonoMod.Tests;

public sealed class ManifestOnMonoTests : IDisposable {
    private static readonly Assembly Manifested = typeof(MonoTarget).Assembly;

    private readonly List<string> log = [];

    public ManifestOnMonoTests() {
        MonoModEntryPoint.ResetForTests();
        MonoModEntryPoint.Install(log.Add, null);
        Patcher.UseLog(log.Add);
        MonoRecorder.Ran.Clear();
        MonoRecorder.SeenBase = 0;
        MonoRecorder.SeenRate = 0f;
    }

    public void Dispose() {
        Patcher.UseLog(null);
        MonoModEntryPoint.ResetForTests();
    }

    [Fact]
    public void Apply_ManifestHeadInjection_RunsBeforeTheTargetBody() {
        Assert.DoesNotContain(typeof(HeadPatch).GetCustomAttributesData(), IsConcordAttribute);

        using (Patcher.Apply(Manifested)) {
            new MonoTarget().Bump();
        }

        Assert.Equal(new[] { "head", "body" }, MonoRecorder.Ran.ToArray());
    }

    [Fact]
    public void Apply_ManifestInjectField_ReadsAndWritesThePrivateTargetField() {
        Assert.DoesNotContain(typeof(FieldPatch).GetCustomAttributesData(), IsConcordAttribute);
        MonoTarget target = new MonoTarget();

        using (Patcher.Apply(Manifested)) {
            Assert.Equal(15, target.Fuel(5));
        }

        Assert.Equal(15, target.FuelSnapshot);
    }

    [Fact]
    public void Apply_ManifestCaptureParameters_GetTheArgumentsOfTheMatchedCall() {
        using (Patcher.Apply(Manifested)) {
            Assert.Equal(8, new MonoTarget().Priced(3));
        }

        Assert.Equal(4, MonoRecorder.SeenBase);
        Assert.Equal(2f, MonoRecorder.SeenRate);
    }

    [Fact]
    public void Apply_ManifestPatchBefore_RunsTheEarlierPatchFirst() {
        using (Patcher.Apply(Manifested)) {
            new MonoTarget().Ordered();
        }

        Assert.Equal(new[] { "early", "late", "body" }, MonoRecorder.Ran.ToArray());
    }

    [Fact]
    public void Dispose_HandleFromManifestApply_RestoresTheTarget() {
        MethodInfo bump = typeof(MonoTarget).GetMethod(nameof(MonoTarget.Bump))!;
        MonoTarget target = new MonoTarget();

        IPatchHandle handle = Patcher.Apply(Manifested);
        Assert.True(PatchBackend.Current.OwnsBody(bump), string.Join("\n", log));
        handle.Dispose();

        target.Bump();
        Assert.Equal(-1, target.Fuel(5));
        Assert.Equal(new[] { "body" }, MonoRecorder.Ran.ToArray());
        Assert.False(PatchBackend.Current.OwnsBody(bump));
    }

    private static bool IsConcordAttribute(CustomAttributeData attribute) {
        return attribute.AttributeType.Namespace == "Concord";
    }
}
