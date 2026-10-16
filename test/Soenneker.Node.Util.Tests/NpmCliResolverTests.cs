using System;
using System.IO;
using System.Threading.Tasks;
using Soenneker.Tests.HostedUnit;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.Directory.Abstract;

namespace Soenneker.Node.Util.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class NpmCliResolverTests(Host host) : HostedUnitTest(host)
{
    [Test]
    [Arguments("lib")]
    [Arguments("lib64")]
    public async Task Finds_versioned_system_npm_layouts(string libraryDirectory)
    {
        var directories = Resolve<IDirectoryUtil>(true);
        var files = Resolve<IFileUtil>(true);
        string root = await directories.CreateTempDirectory();
        try
        {
            string bin = Path.Combine(root, "bin");
            string cli = Path.Combine(root, libraryDirectory, "node_modules", "npm24", "bin", "npm-cli.js");
            await directories.Create(bin);
            await directories.Create(Path.GetDirectoryName(cli)!);
            await files.Write(cli, "");
            await Assert.That(await new NpmCliResolver(files).FindBundled(Path.Combine(bin, "node24"))).IsEqualTo(cli);
        }
        finally
        {
            await directories.Delete(root);
        }
    }

    [Test]
    [Arguments("node_modules/npm/bin/npm-cli.js")]
    [Arguments("../lib/node_modules/npm/bin/npm-cli.js")]
    [Arguments("../share/nodejs/npm/bin/npm-cli.js")]
    public async Task Finds_bundled_and_separate_layouts(string relativeCli)
    {
        string root = Path.Combine(Path.GetTempPath(), "npm resolver " + Guid.NewGuid().ToString("N"));
        string bin = Path.Combine(root, "bin");
        Directory.CreateDirectory(bin);
        try
        {
            string cli = Path.GetFullPath(Path.Combine(bin, relativeCli));
            Directory.CreateDirectory(Path.GetDirectoryName(cli)!);
            File.WriteAllText(cli, "");
            string npm = Path.Combine(bin, "npm.cmd");
            File.WriteAllText(npm, "");
            if (await new NpmCliResolver(Resolve<IFileUtil>(true)).FindBundled(Path.Combine(bin, "node")) != cli || await new NpmCliResolver(Resolve<IFileUtil>(true)).FindSeparate(npm) != cli)
                throw new InvalidOperationException("Failed to resolve npm layout: " + relativeCli);
            File.Delete(cli);
            if (await new NpmCliResolver(Resolve<IFileUtil>(true)).FindSeparate(npm) is not null)
                throw new InvalidOperationException("A wrapper without an npm CLI should not resolve.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task Missing_npm_does_not_resolve_from_working_directory()
    {
        if (await new NpmCliResolver(Resolve<IFileUtil>(true)).FindSeparate("npm") is not null)
            throw new InvalidOperationException("An unresolved command must not be treated as a filesystem path.");
    }

    [Test]
    public async Task Resolves_unix_npm_symlinks()
    {
        // Windows developer-mode/admin symlink privileges are not required by this suite.
        if (OperatingSystem.IsWindows())
            TUnit.Core.Skip.Test("Unix symlink layout is exercised by the Ubuntu and macOS CI jobs.");
        string root = Path.Combine(Path.GetTempPath(), "npm links " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string cli = Path.Combine(root, "separate", "npm-cli.js");
            Directory.CreateDirectory(Path.GetDirectoryName(cli)!);
            File.WriteAllText(cli, "");
            string link = Path.Combine(root, "npm");
            File.CreateSymbolicLink(link, Path.Combine("separate", "npm-cli.js"));
            if (await new NpmCliResolver(Resolve<IFileUtil>(true)).FindSeparate(link) != cli)
                throw new InvalidOperationException("The npm symlink target was not resolved.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
