using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Soenneker.Extensions.ValueTask;
using Soenneker.Node.Util.Abstract;
using Soenneker.Utils.Process.Abstract;
using Soenneker.Utils.Runtime;

namespace Soenneker.Node.Util;

/// <inheritdoc cref="INodeUtil"/>
public sealed class NodeUtil : INodeUtil
{
    private readonly IProcessUtil _processUtil;
    private readonly ILogger<NodeUtil> _logger;

    public NodeUtil(IProcessUtil processUtil, ILogger<NodeUtil> logger)
    {
        _processUtil = processUtil;
        _logger = logger;
    }

    public async ValueTask<string> GetNodePath(string nodeCommand = "node", CancellationToken cancellationToken = default)
    {
        string result = await _processUtil.StartAndGetOutput(
            nodeCommand,
            "-e \"console.log(process.execPath)\"",
            "",
            TimeSpan.FromSeconds(5),
            cancellationToken
        ).NoSync();

        return result.Trim();
    }

    public async ValueTask<string> EnsureInstalled(string? minVersion = null, bool installIfMissing = true, CancellationToken cancellationToken = default)
    {
        bool anyVersion = string.IsNullOrWhiteSpace(minVersion);
        _logger.LogInformation("Ensuring Node.js {Version} is installed.", anyVersion ? "any (latest)" : minVersion);

        if (anyVersion)
        {
            if (await TryLocateAny(cancellationToken).NoSync() is { } anyPath)
            {
                await LogVersionAndReturn(anyPath, cancellationToken);
                return anyPath;
            }

            if (installIfMissing)
            {
                await TryInstall(null, cancellationToken).NoSync();
                if (await TryLocateAny(cancellationToken).NoSync() is { } installedAny)
                {
                    await LogVersionAndReturn(installedAny, cancellationToken);
                    return installedAny;
                }
            }

            throw new InvalidOperationException("Node.js not found.");
        }

        if (!TryParseVersion(minVersion!, out Version? required))
            throw new ArgumentException($"Bad version string \"{minVersion}\".", nameof(minVersion));

        if (await TryLocate(minVersion, cancellationToken).NoSync() is { } path)
        {
            await LogVersionAndReturn(path, cancellationToken);
            return path;
        }

        if (installIfMissing)
        {
            await TryInstall(required!, cancellationToken).NoSync();
            if (await TryLocate(minVersion, cancellationToken).NoSync() is { } installed)
            {
                await LogVersionAndReturn(installed, cancellationToken);
                return installed;
            }
        }

        throw new InvalidOperationException($"Node.js {minVersion} not found.");
    }

    private async ValueTask LogVersionAndReturn(string nodePath, CancellationToken cancellationToken)
    {
        string? version = await GetVersionAtPath(nodePath, cancellationToken).NoSync();
        if (version is { } v)
            _logger.LogInformation("Node.js found at {Path}, version {Version}.", nodePath, v);
    }

    private async ValueTask<string?> GetVersionAtPath(string nodePath, CancellationToken ct)
    {
        try
        {
            string output = await _processUtil.StartAndGetOutput(
                nodePath,
                "-e \"console.log(process.version)\"",
                "",
                TimeSpan.FromSeconds(5),
                ct
            ).NoSync();
            return output.Trim();
        }
        catch
        {
            return null;
        }
    }

    public async ValueTask<string?> TryLocate(string? minVersion = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(minVersion))
            return await TryLocateAny(cancellationToken).NoSync();

        if (!TryParseVersion(minVersion, out Version? required))
            return null;

        if (RuntimeUtil.IsWindows())
        {
            if (ProbeHostedToolCache(required!) is { } cached)
                return cached;
        }

        string[] commands = OperatingSystem.IsWindows()
            ? ["node", "node.exe"]
            : ["node", "nodejs"];

        foreach (string cmd in commands)
        {
            if (await Probe(cmd, required!, cancellationToken).NoSync() is { } found)
                return found;
        }

        return null;
    }

    public async ValueTask<string?> TryLocateAny(CancellationToken cancellationToken = default)
    {
        if (RuntimeUtil.IsWindows())
        {
            if (ProbeHostedToolCacheAny() is { } cached)
                return cached;
        }

        string[] commands = OperatingSystem.IsWindows()
            ? ["node", "node.exe"]
            : ["node", "nodejs"];

        foreach (string cmd in commands)
        {
            if (await ProbeAny(cmd, cancellationToken).NoSync() is { } found)
                return found;
        }

        return null;
    }

    public async ValueTask TryInstall(Version? version, CancellationToken cancellationToken = default)
    {
        bool latest = version is null;
        int major = version?.Major ?? 0;
        var ver = major.ToString();

        if (RuntimeUtil.IsLinux())
        {
            try
            {
                await _processUtil.BashRun(
                    "sudo apt-get -qq update && sudo apt-get -y install nodejs",
                    "",
                    cancellationToken: cancellationToken
                ).NoSync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "apt-get install nodejs failed (node may already be installed or install may require privileges).");
            }
        }
        else if (RuntimeUtil.IsWindows())
        {
            string wingetId = latest ? "OpenJS.NodeJS" : $"OpenJS.NodeJS.{major}";
            string wingetArgs = latest
                ? "install --exact --id OpenJS.NodeJS --silent --disable-interactivity --accept-source-agreements --accept-package-agreements --source winget"
                : $"install --exact --id {wingetId} --silent --disable-interactivity --accept-source-agreements --accept-package-agreements --source winget";

            if (await _processUtil.CommandExistsAndRuns("winget", "--version", TimeSpan.FromSeconds(3), cancellationToken).NoSync())
            {
                try
                {
                    await _processUtil.StartAndGetOutput(
                        "winget",
                        wingetArgs,
                        "",
                        TimeSpan.FromMinutes(5),
                        cancellationToken
                    ).NoSync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "winget install {WingetId} failed (node may already be installed or install may require elevation).", wingetId);
                }
            }
            else if (await _processUtil.CommandExistsAndRuns("choco", "--version", TimeSpan.FromSeconds(3), cancellationToken).NoSync())
            {
                try
                {
                    string chocoArgs = latest ? "install nodejs -y --no-progress" : $"install nodejs --version {major}.0.0 -y --no-progress";
                    await _processUtil.StartAndGetOutput(
                        "choco",
                        chocoArgs,
                        "",
                        TimeSpan.FromMinutes(5),
                        cancellationToken
                    ).NoSync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "choco install nodejs failed (node may already be installed or install may require elevation).");
                }
            }
            else
            {
                throw new InvalidOperationException("Neither winget nor Chocolatey is available to install Node.js on this runner.");
            }
        }
        else if (RuntimeUtil.IsMacOs())
        {
            try
            {
                string brewArgs = latest ? "install node" : $"install node@{ver}";
                await _processUtil.StartAndGetOutput(
                    "brew",
                    brewArgs,
                    "",
                    TimeSpan.FromMinutes(10),
                    cancellationToken
                ).NoSync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "brew install node failed (node may already be installed).");
            }
        }
    }

    private static string? ProbeHostedToolCache(Version target)
    {
        string root = Environment.GetEnvironmentVariable("AGENT_TOOLSDIRECTORY") ?? @"C:\hostedtoolcache\windows";
        string nodeRoot = Path.Combine(root, "Node");
        if (!Directory.Exists(nodeRoot))
            return null;

        foreach (string verDir in Directory.EnumerateDirectories(nodeRoot))
        {
            string dirName = Path.GetFileName(verDir);
            if (string.IsNullOrEmpty(dirName) || dirName.Length < 2)
                continue;
            if (!Version.TryParse(dirName, out Version? v) || !MatchMajorMinor(v, target))
                continue;

            string candidate = Path.Combine(verDir, "x64", "node.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string? ProbeHostedToolCacheAny()
    {
        string root = Environment.GetEnvironmentVariable("AGENT_TOOLSDIRECTORY") ?? @"C:\hostedtoolcache\windows";
        string nodeRoot = Path.Combine(root, "Node");
        if (!Directory.Exists(nodeRoot))
            return null;

        foreach (string verDir in Directory.EnumerateDirectories(nodeRoot))
        {
            string candidate = Path.Combine(verDir, "x64", "node.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private async ValueTask<string?> ProbeAny(string command, CancellationToken ct)
    {
        try
        {
            string output = await _processUtil.StartAndGetOutput(
                command,
                "-e \"console.log(process.execPath)\"",
                "",
                TimeSpan.FromSeconds(5),
                ct
            ).NoSync();
            return output.Trim();
        }
        catch
        {
            return null;
        }
    }

    private async ValueTask<string?> Probe(string command, Version target, CancellationToken ct)
    {
        try
        {
            string output = await _processUtil.StartAndGetOutput(
                command,
                "-e \"console.log(process.execPath + '\\n' + process.version)\"",
                "",
                TimeSpan.FromSeconds(5),
                ct
            ).NoSync();

            string[] lines = output.Trim().Split('\n');
            if (lines.Length < 2)
                return null;

            string execPath = lines[0].Trim();
            string versionStr = lines[^1].Trim();
            if (versionStr.StartsWith('v'))
                versionStr = versionStr[1..];

            if (!Version.TryParse(versionStr, out Version? v))
                return null;

            return MatchMajorMinor(v, target) ? execPath : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool MatchMajorMinor(Version found, Version target) =>
        found.Major == target.Major && (target.Minor < 1 || found.Minor == target.Minor);

    private static bool TryParseVersion(string version, out Version? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(version))
            return false;
        version = version.Trim();
        if (version.StartsWith('v'))
            version = version[1..].Trim();
        // Version.TryParse often requires at least "major.minor"; normalize "20" -> "20.0"
        if (version.Length > 0 && version.IndexOf('.') < 0)
            version += ".0";
        return Version.TryParse(version, out result);
    }
}
