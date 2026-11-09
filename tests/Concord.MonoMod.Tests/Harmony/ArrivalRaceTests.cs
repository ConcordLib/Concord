using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Concord.Backend;
using Concord.MonoMod;
using Concord.MonoMod.Harmony;
using Xunit;

namespace Concord.MonoMod.Tests;

[Collection("HarmonySerial")]
public sealed class ArrivalRaceTests {
    private const int Rounds = 20;

    private const int JoinMs = 30000;

    [Fact]
    public void TwoCopiesArrivingAtOnce_AreEachHookedExactlyOnce() {
        for (int round = 0; round < Rounds; round++) {
            OneRound(round);
        }
    }

    private static void OneRound(int round) {
        List<string> log = new List<string>();

        void Write(string line) {
            lock (log) {
                log.Add(line);
            }
        }

        byte[][] images = {
            WithVersion(AssemblyImage.WithFreshModuleId(HarmonyImage()), new Version(3, 0, round, 1)),
            WithVersion(AssemblyImage.WithFreshModuleId(HarmonyImage()), new Version(3, 0, round, 2)),
        };

        Assembly?[] loaded = new Assembly?[2];
        Exception?[] faults = new Exception?[2];
        Thread[] threads = new Thread[2];

        using (Barrier barrier = new Barrier(2)) {
            try {
                MonoModEntryPoint.Install(Write, null);

                for (int i = 0; i < 2; i++) {
                    int slot = i;
                    threads[slot] = new Thread(() => {
                        try {
                            if (!barrier.SignalAndWait(JoinMs)) {
                                throw new TimeoutException("the other arrival never reached the barrier");
                            }

                            loaded[slot] = Assembly.Load(images[slot]);
                        } catch (Exception ex) {
                            faults[slot] = ex;
                        }
                    }) {
                        IsBackground = true,
                        Name = "arrival-" + round + "-" + slot,
                    };
                }

                threads[0].Start();
                threads[1].Start();

                for (int i = 0; i < 2; i++) {
                    Assert.True(
                        threads[i].Join(JoinMs),
                        At(round, i) + " never returned from Assembly.Load, so two overlapping arrivals deadlocked");
                }

                for (int i = 0; i < 2; i++) {
                    Assert.True(faults[i] == null, At(round, i) + " threw: " + faults[i]);
                    Assert.True(loaded[i] != null, At(round, i) + " loaded no assembly");
                }

                string[] lines = Lines(log);

                for (int i = 0; i < 2; i++) {
                    Assembly copy = loaded[i]!;
                    Assert.True(
                        UpdateWrapperHook.IsHookedOn(copy),
                        At(round, i) + " loaded " + Describe(copy) + " and it never got a notifier detour:\n" + string.Join("\n", lines));

                    string installed = CoexistenceLogMarkers.HookInstalled + " " + Describe(copy);
                    int hooks = 0;
                    foreach (string line in lines) {
                        if (string.Equals(line, installed, StringComparison.Ordinal)) {
                            hooks++;
                        }
                    }

                    Assert.True(
                        hooks == 1,
                        At(round, i) + " logged " + hooks + " hook-installed lines for " + Describe(copy) + ", expected 1:\n" + string.Join("\n", lines));
                }

                foreach (string line in lines) {
                    Assert.False(
                        line.StartsWith(HarmonyArrival.ArmFailedMarker, StringComparison.Ordinal),
                        "round " + round + " failed to arm: " + line);
                }
            } finally {
                MonoModEntryPoint.ResetForTests();
            }
        }
    }

    private static string At(int round, int slot) {
        return "round " + round + " thread " + slot;
    }

    private static string[] Lines(List<string> log) {
        lock (log) {
            return log.ToArray();
        }
    }

    private static string Describe(Assembly copy) {
        Version version = copy.GetName().Version ?? throw new InvalidOperationException(copy.FullName + " has no assembly version");
        return version + " #" + copy.GetHashCode();
    }

    private static byte[] HarmonyImage() {
        return File.ReadAllBytes(typeof(HarmonyLib.Harmony).Assembly.Location);
    }

    private static byte[] WithVersion(byte[] image, Version to) {
        Version from = typeof(HarmonyLib.Harmony).Assembly.GetName().Version
                       ?? throw new InvalidOperationException("the running 0Harmony has no assembly version");
        byte[] row = VersionRow(0x8004, from);
        int hit = -1;
        for (int i = 0; i <= image.Length - row.Length; i++) {
            if (Matches(image, i, row)) {
                Assert.True(hit < 0, "the Assembly table version row pattern matched more than once");
                hit = i;
            }
        }

        Assert.True(hit >= 0, "the Assembly table version row for " + from + " was not found in the image");
        VersionRow(0x8004, to).CopyTo(image, hit);
        return image;
    }

    private static byte[] VersionRow(int hashAlgId, Version version) {
        byte[] row = new byte[12];
        BitConverter.GetBytes(hashAlgId).CopyTo(row, 0);
        BitConverter.GetBytes((ushort)version.Major).CopyTo(row, 4);
        BitConverter.GetBytes((ushort)version.Minor).CopyTo(row, 6);
        BitConverter.GetBytes((ushort)version.Build).CopyTo(row, 8);
        BitConverter.GetBytes((ushort)version.Revision).CopyTo(row, 10);
        return row;
    }

    private static bool Matches(byte[] image, int offset, byte[] pattern) {
        for (int i = 0; i < pattern.Length; i++) {
            if (image[offset + i] != pattern[i]) {
                return false;
            }
        }

        return true;
    }
}
