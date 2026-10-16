using System;
using System.IO;
using System.Threading.Tasks;
using Soenneker.Node.Util.Abstract;
using Soenneker.Tests.HostedUnit;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.File.Abstract;

namespace Soenneker.Node.Util.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class LinuxNodeInstallationTests(Host host) : HostedUnitTest(host)
{
    [Test]
    public async Task Installs_and_reuses_Node_with_the_native_package_manager()
    {
        // This installation test runs only in the isolated Linux container matrix.
        if (Environment.GetEnvironmentVariable("NODE_UTIL_INSTALL_SMOKE") != "1")
            return;
        if (!OperatingSystem.IsLinux())
            throw new InvalidOperationException("The installation smoke test requires Linux.");

        var util = Resolve<INodeUtil>(true);
        // Another test process may already have installed Node in this container.
        string node = await util.EnsureInstalled();
        await Assert.That(await util.TryLocate("24")).IsEqualTo(node);
        await Assert.That(await util.EnsureInstalled(installIfMissing: false)).IsEqualTo(node);
        var fileUtil = Resolve<IFileUtil>(true);
        var directoryUtil = Resolve<IDirectoryUtil>(true);
        await Assert.That(await fileUtil.Exists(await util.GetNpmPath())).IsTrue();
        await Assert.That(await fileUtil.Exists(await util.GetNpxPath())).IsTrue();

        string project = await directoryUtil.CreateTempDirectory();
        try
        {
            await fileUtil.Write(Path.Combine(project, "package.json"), """{"name":"installation-smoke","version":"1.0.0","private":true}""");
            await util.NpmInstall(project, ignoreScripts: true);
            await Assert.That(await fileUtil.Exists(Path.Combine(project, "npm-install.lockhash"))).IsTrue();
        }
        finally
        {
            await directoryUtil.Delete(project);
        }
    }
}
