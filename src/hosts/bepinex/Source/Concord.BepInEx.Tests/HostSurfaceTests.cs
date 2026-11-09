using System;
using System.Collections.Generic;
using Concord.BepInEx;
using Xunit;

namespace Concord.BepInEx.Tests;

[Collection("AdapterWiringSerial")]
public class HostSurfaceTests {
    [Fact]
    public void Report_ReadAtCallTime() {
        List<string> first = new List<string>();
        List<string> second = new List<string>();
        HostSurface.Report = first.Add;
        Action<string> captured = message => HostSurface.Report(message);

        try {
            HostSurface.Report = second.Add;
            captured("hello");

            Assert.Empty(first);
            Assert.Single(second);
        } finally {
            HostSurface.ResetForTests();
        }
    }
}
