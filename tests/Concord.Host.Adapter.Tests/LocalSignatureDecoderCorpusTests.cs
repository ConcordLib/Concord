using System.Reflection;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using ReflectedBody = System.Reflection.MethodBody;

namespace Concord.Host.Adapter.Tests;

public class LocalSignatureDecoderCorpusTests {
    private const int Reported = 16;

    private readonly ITestOutputHelper output;

    public LocalSignatureDecoderCorpusTests(ITestOutputHelper output) {
        this.output = output;
    }

    [Theory]
    [MemberData(nameof(RealBodies.Corpora), MemberType = typeof(RealBodies))]
    public void EverySignatureDecodesToTheSlotTypesTheRuntimeReports(string corpus) {
        Tally tally = Measure(corpus);

        output.WriteLine(tally.Describe());

        Assert.True(
            tally.Signatures > 1000,
            $"corpus '{corpus}' only reached {tally.Signatures} local signatures, too few to prove anything");
        Assert.True(
            tally.Refused == tally.FunctionPointers,
            $"the decoder refused {tally.Refused - tally.FunctionPointers} signatures for a reason other than "
            + $"a function-pointer local, which LocalSignatureEncoder refuses too\n{tally.Describe()}");
        Assert.True(tally.Mismatched == 0, $"the decoder disagreed on {tally.Mismatched} signatures\n{tally.Describe()}");
    }

    [Theory]
    [MemberData(nameof(RealBodies.Corpora), MemberType = typeof(RealBodies))]
    public void TheCorpusCoversTheConstructedShapesWorthDecoding(string corpus) {
        Tally tally = Measure(corpus);

        Assert.True(tally.Generic > 50, $"only {tally.Generic} slots held a constructed generic\n{tally.Describe()}");
        Assert.True(tally.Parameter > 50, $"only {tally.Parameter} slots held a generic parameter\n{tally.Describe()}");
        Assert.True(tally.Array > 50, $"only {tally.Array} slots held an array\n{tally.Describe()}");
        Assert.True(tally.ByRefOrPointer > 10, $"only {tally.ByRefOrPointer} slots held a byref or pointer\n{tally.Describe()}");
    }

    private static Tally Measure(string corpus) {
        Tally tally = new Tally();
        foreach (Assembly assembly in RealBodies.Of(corpus)) {
            foreach (MethodBase method in RealBodies.Methods(assembly)) {
                Judge(method, tally);
            }
        }

        return tally;
    }

    private static void Judge(MethodBase method, Tally tally) {
        ReflectedBody? reflected;
        byte[]? signature;
        try {
            reflected = method.GetMethodBody();
            if (reflected is null || reflected.LocalVariables.Count == 0) {
                return;
            }

            signature = method.Module.ResolveSignature(reflected.LocalSignatureMetadataToken);
        } catch (Exception unreadable) when (unreadable is not OutOfMemoryException) {
            tally.Unreadable++;
            return;
        }

        if (signature is null || signature.Length == 0) {
            tally.Unreadable++;
            return;
        }

        tally.Signatures++;
        ComposedLocal[]? decoded = LocalSignatureDecoder.Decode(signature, method, out string? refusal);

        if (decoded is null) {
            tally.Refused++;
            if (refusal?.Contains("function pointer", StringComparison.Ordinal) == true) {
                tally.FunctionPointers++;
                return;
            }

            tally.Note($"{Describe(method)}: {refusal}");
            return;
        }

        List<Type> expected = reflected.LocalVariables
            .OrderBy(local => local.LocalIndex)
            .Select(local => local.LocalType)
            .ToList();

        if (decoded.Length != expected.Count) {
            tally.Mismatched++;
            tally.Note($"{Describe(method)}: decoded {decoded.Length} slots, the runtime reports {expected.Count}");
            return;
        }

        for (int slot = 0; slot < decoded.Length; slot++) {
            tally.Count(decoded[slot].Type);
            if (decoded[slot].Type == expected[slot]) {
                continue;
            }

            tally.Mismatched++;
            tally.Note($"{Describe(method)} slot {slot}: decoded '{decoded[slot].Type}', the runtime reports '{expected[slot]}'");
            return;
        }
    }

    private static string Describe(MethodBase method) {
        return (method.DeclaringType?.FullName ?? "<no type>") + "." + method.Name;
    }

    private sealed class Tally {
        private readonly List<string> notes = [];

        internal int Signatures { get; set; }

        internal int Unreadable { get; set; }

        internal int Refused { get; set; }

        internal int FunctionPointers { get; set; }

        internal int Mismatched { get; set; }

        internal int Generic { get; private set; }

        internal int Parameter { get; private set; }

        internal int Array { get; private set; }

        internal int ByRefOrPointer { get; private set; }

        internal void Count(Type slot) {
            if (slot.IsByRef || slot.IsPointer) {
                ByRefOrPointer++;
                return;
            }

            if (slot.IsArray) {
                Array++;
                return;
            }

            if (slot.IsGenericParameter) {
                Parameter++;
                return;
            }

            if (slot.IsGenericType) {
                Generic++;
            }
        }

        internal void Note(string line) {
            if (notes.Count < Reported) {
                notes.Add(line);
            }
        }

        internal string Describe() {
            StringBuilder text = new StringBuilder();
            text.AppendLine($"signatures {Signatures}, refused {Refused} ({FunctionPointers} function pointers), "
                + $"mismatched {Mismatched}, unreadable {Unreadable}");
            text.AppendLine($"slots: generic {Generic}, parameter {Parameter}, array {Array}, byref or pointer {ByRefOrPointer}");
            foreach (string note in notes) {
                text.AppendLine("  " + note);
            }

            return text.ToString();
        }
    }
}
