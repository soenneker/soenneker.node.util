using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Soenneker.Node.Util.Tests;

public sealed class LinuxNodeDistributionTests
{
    [Test]
    [Arguments(Architecture.X64, false, "linux-x64")]
    [Arguments(Architecture.Arm64, false, "linux-arm64")]
    [Arguments(Architecture.X64, true, "linux-x64-musl")]
    [Arguments(Architecture.Arm64, true, "linux-arm64-musl")]
    [Arguments(Architecture.S390x, false, "linux-s390x")]
    public async Task Target_matches_architecture_and_libc(Architecture architecture, bool musl, string expected)
    {
        await Assert.That(LinuxNodeDistribution.GetTarget(architecture, musl)).IsEqualTo(expected);
    }

    [Test]
    public async Task Release_selection_respects_minimum_major_architecture_and_libc()
    {
        const string index = """
            [
              {"version":"v26.1.0","files":["linux-x64-musl"]},
              {"version":"v24.1.0","files":["linux-x64-musl"]},
              {"version":"v24.4.0","files":["linux-x64"]},
              {"version":"v24.3.0","files":["linux-x64-musl"]},
              {"version":"v24.5.0-rc.1","files":["linux-x64-musl"]}
            ]
            """;
        LinuxNodeRelease? release = LinuxNodeDistribution.SelectRelease(index, new Version(24, 2), "linux-x64-musl", LinuxNodeDistribution.OfficialSource);
        await Assert.That(release!.Version).IsEqualTo(new Version(24, 3, 0));
        await Assert.That(LinuxNodeDistribution.SelectRelease(index, new Version(24, 4), "linux-x64-musl", LinuxNodeDistribution.OfficialSource)).IsNull();
        await Assert.That(LinuxNodeDistribution.SelectRelease(index, null, "linux-arm64", LinuxNodeDistribution.OfficialSource)).IsNull();
    }
}
