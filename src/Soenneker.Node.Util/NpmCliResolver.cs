using System;
using System.IO;

namespace Soenneker.Node.Util;

/// <summary>Locates npm CLI scripts in bundled and separate npm installations.</summary>
public static class NpmCliResolver
{
    /// <summary>Finds npm-cli.js near the Node executable or its symlink target, returning null when absent.</summary>
    public static string? FindBundled(string nodePath)
    {
        string? result = FindNear(nodePath);
        if (result is not null)
            return result;

        string? target = ResolveLink(nodePath);
        return target is null ? null : FindNear(target);
    }

    /// <summary>Finds npm-cli.js from an absolute npm executable path, returning null when absent.</summary>
    public static string? FindSeparate(string npmPath)
    {
        // A bare command means GetNpmPath did not find an installation.
        if (!Path.IsPathFullyQualified(npmPath) || !File.Exists(npmPath))
            return null;

        string? target = ResolveLink(npmPath);
        if (target is not null && Path.GetFileName(target) == "npm-cli.js" && File.Exists(target))
            return target;
        if (Path.GetFileName(npmPath) == "npm-cli.js")
            return npmPath;

        return FindNear(npmPath) ?? (target is null ? null : FindNear(target));
    }

    private static string? FindNear(string executable)
    {
        string? directory = Path.GetDirectoryName(executable);
        if (string.IsNullOrEmpty(directory))
            return null;

        // Windows archives/global npm installs, Unix archives/nvm/Homebrew,
        // and Debian/Ubuntu's separately packaged npm respectively.
        string[] candidates =
        [
            Path.Combine(directory, "node_modules", "npm", "bin", "npm-cli.js"),
            Path.Combine(directory, "..", "lib", "node_modules", "npm", "bin", "npm-cli.js"),
            Path.Combine(directory, "..", "share", "nodejs", "npm", "bin", "npm-cli.js")
        ];

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
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
