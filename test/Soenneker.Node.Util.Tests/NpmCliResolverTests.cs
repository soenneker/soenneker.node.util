using System;
using System.IO;

namespace Soenneker.Node.Util.Tests;

public sealed class NpmCliResolverTests
{
    [Test]
    [Arguments("node_modules/npm/bin/npm-cli.js")]
    [Arguments("../lib/node_modules/npm/bin/npm-cli.js")]
    [Arguments("../share/nodejs/npm/bin/npm-cli.js")]
    public void Finds_bundled_and_separate_layouts(string relativeCli)
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
            if (NpmCliResolver.FindBundled(Path.Combine(bin, "node")) != cli || NpmCliResolver.FindSeparate(npm) != cli)
                throw new InvalidOperationException("Failed to resolve npm layout: " + relativeCli);
            File.Delete(cli);
            if (NpmCliResolver.FindSeparate(npm) is not null)
                throw new InvalidOperationException("A wrapper without an npm CLI should not resolve.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public void Missing_npm_does_not_resolve_from_working_directory()
    {
        if (NpmCliResolver.FindSeparate("npm") is not null)
            throw new InvalidOperationException("An unresolved command must not be treated as a filesystem path.");
    }

    [Test]
    public void Resolves_unix_npm_symlinks()
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
            if (NpmCliResolver.FindSeparate(link) != cli)
                throw new InvalidOperationException("The npm symlink target was not resolved.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
