using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public static class Readable {
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Body() {
        return 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Guarded(int n) {
        int held = n;
        try {
            held = checked(held * 2);
        } catch (OverflowException) {
            held = -1;
        } finally {
            held += 1;
        }

        return held;
    }
}

public sealed class HostBodyReadTests : IDisposable {
    private const string NoRead = "This runtime has no host body read, so there is nothing to compare.";

    private static readonly MethodInfo Target = typeof(Readable).GetMethod(nameof(Readable.Body))!;

    private static readonly MethodInfo Guarded = typeof(Readable).GetMethod(nameof(Readable.Guarded))!;

    private bool written;

    public static bool Unavailable => ForkRuntime.Unavailable || !ForkBodyReader.Shared.IsAvailable;

    public void Dispose() {
        if (written) {
            ForkRuntime.Restore(Target);
        }
    }

    [Fact(Skip = NoRead, SkipWhen = nameof(Unavailable))]
    public void TheHostOriginalReadKeepsReturningTheDeclaredIlAfterTheBodyIsReplaced() {
        HostBody? before = ForkBodyReader.Shared.Read(Target);
        byte[]? reflectedBefore = Target.GetMethodBody()?.GetILAsByteArray();

        ForkRuntime.Write(Target, 5);
        written = true;
        Assert.Equal(5, Readable.Body());

        HostBody? after = ForkBodyReader.Shared.Read(Target);
        byte[]? reflectedAfter = Target.GetMethodBody()?.GetILAsByteArray();

        string report =
            $"host: {Show(before?.Il)} -> {Show(after?.Il)}. " +
            $"GetMethodBody: {Show(reflectedBefore)} -> {Show(reflectedAfter)}.";

        Assert.True(before is not null, "the host refused to read an untouched body at all. " + report);
        Assert.True(
            reflectedBefore is not null && before!.Il.SequenceEqual(reflectedBefore),
            "the host read does not match the IL reflection returned before any write, so it is some other " +
            "snapshot rather than the method's declared body. " + report);
        Assert.True(
            after is not null && before!.Il.SequenceEqual(after.Il),
            "the host read changed when the body was replaced, so it reports the live body and Concord has no " +
            "way to ask for a method's original IL. " + report);
        Assert.True(
            reflectedAfter is not null && !reflectedBefore!.SequenceEqual(reflectedAfter),
            "reflection returned the same IL after the write, so this runtime does not read bodies back live " +
            "and this fact does not measure what it claims. " + report);
    }

    [Fact(Skip = NoRead, SkipWhen = nameof(Unavailable))]
    public void TheHostReadCarriesTheHeaderValuesABodyNeedsToBeRebuilt() {
        HostBody read = ForkBodyReader.Shared.Read(Guarded)!;
        MethodBody reflected = Guarded.GetMethodBody()!;

        Assert.Equal(reflected.MaxStackSize, read.MaxStackSize);
        Assert.Equal(reflected.InitLocals, read.InitLocals);
        Assert.Equal(reflected.LocalSignatureMetadataToken, read.LocalSignatureToken);
        Assert.Equal(reflected.ExceptionHandlingClauses.Count, read.Clauses.Count);
        Assert.NotEmpty(read.LocalSignature);

        foreach (HostClause clause in read.Clauses) {
            Assert.Contains(
                reflected.ExceptionHandlingClauses,
                found => found.TryOffset == clause.TryOffset
                    && found.TryLength == clause.TryLength
                    && found.HandlerOffset == clause.HandlerOffset
                    && found.HandlerLength == clause.HandlerLength);
        }
    }

    private static string Show(byte[]? body) {
        return body is null ? "refused" : $"[{string.Join(",", body.Take(8))}] of {body.Length}";
    }
}
