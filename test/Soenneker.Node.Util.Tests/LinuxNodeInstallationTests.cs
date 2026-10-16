using System;
using System.IO;
using System.Threading.Tasks;
using Soenneker.Node.Util.Abstract;
using Soenneker.Tests.HostedUnit;

namespace Soenneker.Node.Util.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class LinuxNodeInstallationTests(Host host) : HostedUnitTest(host)
{
    [Test]
    public async Task Installs_and_reuses_Node_without_root_or_package_managers()
    {
        // This installation test runs only in the isolated, unprivileged Linux container matrix.
        if (Environment.GetEnvironmentVariable("NODE_UTIL_INSTALL_SMOKE") != "1")
            return;
        if (!OperatingSystem.IsLinux())
            throw new InvalidOperationException("The installation smoke test requires Linux.");

        var util = Resolve<INodeUtil>(true);
        await Assert.That(await util.TryLocateAny()).IsNull();
        string node = await util.EnsureInstalled();
        await Assert.That(node.StartsWith(LinuxNodeDistribution.CacheDirectory, StringComparison.Ordinal)).IsTrue();
        await Assert.That(await util.TryLocate("24")).IsEqualTo(node);
        await Assert.That(await util.EnsureInstalled(installIfMissing: false)).IsEqualTo(node);
        await Assert.That(File.Exists(await util.GetNpmPath())).IsTrue();
        await Assert.That(File.Exists(await util.GetNpxPath())).IsTrue();

        string project = Path.Combine(Path.GetTempPath(), "node installation " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(project);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(project, "package.json"), """{"name":"installation-smoke","version":"1.0.0","private":true}""");
            await util.NpmInstall(project, ignoreScripts: true);
            await Assert.That(File.Exists(Path.Combine(project, "npm-install.lockhash"))).IsTrue();
        }
        finally
        {
            Directory.Delete(project, recursive: true);
        }
    }
}
