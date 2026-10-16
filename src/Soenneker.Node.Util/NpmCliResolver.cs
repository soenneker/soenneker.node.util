using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Extensions.ValueTask;
using Soenneker.Utils.File.Abstract;

namespace Soenneker.Node.Util;

/// <summary>Locates npm CLI scripts in bundled and separate npm installations.</summary>
public sealed class NpmCliResolver(IFileUtil fileUtil)
{
    /// <summary>Finds npm-cli.js near the Node executable or its symlink target, returning null when absent.</summary>
    public async ValueTask<string?> FindBundled(string nodePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? result = await FindNear(nodePath, cancellationToken).NoSync();
        if (result is not null)
            return result;

        string? target = ResolveLink(nodePath);
        return target is null ? null : await FindNear(target, cancellationToken).NoSync();
    }

    /// <summary>Finds npm-cli.js from an absolute npm executable path, returning null when absent.</summary>
    public async ValueTask<string?> FindSeparate(string npmPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A bare command means GetNpmPath did not find an installation.
        if (!Path.IsPathFullyQualified(npmPath) || !await fileUtil.Exists(npmPath, cancellationToken).NoSync())
            return null;

        string? target = ResolveLink(npmPath);
        if (target is not null && Path.GetFileName(target) == "npm-cli.js" && await fileUtil.Exists(target, cancellationToken).NoSync())
            return target;
        if (Path.GetFileName(npmPath) == "npm-cli.js")
            return npmPath;

        return await FindNear(npmPath, cancellationToken).NoSync() ??
               (target is null ? null : await FindNear(target, cancellationToken).NoSync());
    }

    private async ValueTask<string?> FindNear(string executable, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(executable);
        if (string.IsNullOrEmpty(directory))
            return null;

        string name = Path.GetFileNameWithoutExtension(executable);
        int suffixStart = name.Length;
        while (suffixStart > 0 && char.IsAsciiDigit(name[suffixStart - 1]))
            suffixStart--;
        string versionSuffix = name[suffixStart..];

        // Include versioned distro packages and both Unix library-directory conventions.
        string[] candidates =
        [
            Path.Combine(directory, "node_modules", "npm", "bin", "npm-cli.js"),
            Path.Combine(directory, "..", "lib", "node_modules", "npm", "bin", "npm-cli.js"),
            Path.Combine(directory, "..", "lib64", "node_modules", "npm", "bin", "npm-cli.js"),
            Path.Combine(directory, "..", "lib", "node_modules", "npm" + versionSuffix, "bin", "npm-cli.js"),
            Path.Combine(directory, "..", "lib64", "node_modules", "npm" + versionSuffix, "bin", "npm-cli.js"),
            Path.Combine(directory, "..", "share", "nodejs", "npm", "bin", "npm-cli.js")
        ];

        foreach (string candidate in candidates)
        {
            if (await fileUtil.Exists(candidate, cancellationToken).NoSync())
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    private static string? ResolveLink(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
