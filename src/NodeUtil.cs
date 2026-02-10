using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Soenneker.Extensions.ValueTask;
using Soenneker.Node.Util.Abstract;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.Process.Abstract;
using System.Collections.Generic;

namespace Soenneker.Node.Util;

/// <inheritdoc cref="INodeUtil"/>
public sealed class NodeUtil : INodeUtil
{
    private static readonly TimeSpan _probeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _existsTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan _installTimeoutWin = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _installTimeoutMac = TimeSpan.FromMinutes(10);

    private const string _scriptExecPath = "-e \"console.log(process.execPath)\"";
    private const string _scriptVersion = "-e \"console.log(process.version)\"";
    private const string _scriptExecPathAndVersion = "-e \"console.log(process.execPath + '\\n' + process.version)\"";

    private static readonly string[] _nodeCommandsWindows = ["node", "node.exe"];
    private static readonly string[] _nodeCommandsUnix = ["node", "nodejs"];

    private static readonly string[] _npxNamesWindows = ["npx.cmd", "npx.exe", "npx"];
    private static readonly string[] _npxNamesUnix = ["npx"];

    private readonly IProcessUtil _processUtil;
    private readonly ILogger<NodeUtil> _logger;
    private readonly IDirectoryUtil _directoryUtil;

    public NodeUtil(IProcessUtil processUtil, ILogger<NodeUtil> logger, IDirectoryUtil directoryUtil)
    {
        _processUtil = processUtil;
        _logger = logger;
        _directoryUtil = directoryUtil;
    }

    public string GetNpxPath()
    {
        ReadOnlySpan<string> npxNames = OperatingSystem.IsWindows() ? _npxNamesWindows : _npxNamesUnix;

        try
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                char sep = Path.PathSeparator;
                ReadOnlySpan<char> span = pathEnv.AsSpan();

                while (!span.IsEmpty)
                {
                    int idx = span.IndexOf(sep);
                    ReadOnlySpan<char> dirSpan = idx >= 0 ? span[..idx] : span;

                    // advance span
                    span = idx >= 0 ? span[(idx + 1)..] : ReadOnlySpan<char>.Empty;

                    dirSpan = dirSpan.Trim();
                    if (dirSpan.IsEmpty)
                        continue;

                    // One string allocation here (dir) only for non-empty dirs we actually probe
                    string dir = dirSpan.ToString();

                    for (int i = 0; i < npxNames.Length; i++)
                    {
                        string full = Path.Combine(dir, npxNames[i]);
                        if (File.Exists(full))
                            return full;
                    }
                }
            }
        }
        catch
        {
            /* ignore */
        }

        if (OperatingSystem.IsWindows())
        {
            // Probe common Windows locations without building a list
            string? programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
            if (!string.IsNullOrEmpty(programFiles))
            {
                string c1 = Path.Combine(programFiles, "nodejs", "npx.cmd");
                if (File.Exists(c1)) return c1;

                string c2 = Path.Combine(programFiles, "nodejs", "npx.exe");
                if (File.Exists(c2)) return c2;
            }

            string? localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (!string.IsNullOrEmpty(localAppData))
            {
                string c1 = Path.Combine(localAppData, "Programs", "node", "npx.cmd");
                if (File.Exists(c1)) return c1;

                string c2 = Path.Combine(localAppData, "Programs", "node", "npx.exe");
                if (File.Exists(c2)) return c2;
            }

            string? appData = Environment.GetEnvironmentVariable("APPDATA");
            if (!string.IsNullOrEmpty(appData))
            {
                string c = Path.Combine(appData, "npm", "npx.cmd");
                if (File.Exists(c)) return c;
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            const string c1 = "/usr/local/bin/npx";
            if (File.Exists(c1)) return c1;

            const string c2 = "/opt/homebrew/bin/npx";
            if (File.Exists(c2)) return c2;
        }
        else if (OperatingSystem.IsLinux())
        {
            const string c1 = "/usr/bin/npx";
            if (File.Exists(c1)) return c1;

            const string c2 = "/usr/local/bin/npx";
            if (File.Exists(c2)) return c2;
        }

        return "npx";
    }

    public async ValueTask<string> GetNodePath(string nodeCommand = "node", CancellationToken cancellationToken = default)
    {
        string result = await _processUtil.StartAndGetOutput(
            nodeCommand,
            _scriptExecPath,
            "",
            _probeTimeout,
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
                await LogVersion(anyPath, cancellationToken);
                return anyPath;
            }

            if (installIfMissing)
            {
                await TryInstall(null, cancellationToken).NoSync();

                if (await TryLocateAny(cancellationToken).NoSync() is { } installedAny)
                {
                    await LogVersion(installedAny, cancellationToken);
                    return installedAny;
                }
            }

            throw new InvalidOperationException("Node.js not found.");
        }

        if (!TryParseVersion(minVersion!, out Version? required))
            throw new ArgumentException($"Bad version string \"{minVersion}\".", nameof(minVersion));

        if (await TryLocate(minVersion, cancellationToken).NoSync() is { } path)
        {
            await LogVersion(path, cancellationToken);
            return path;
        }

        if (installIfMissing)
        {
            await TryInstall(required!, cancellationToken).NoSync();

            if (await TryLocate(minVersion, cancellationToken).NoSync() is { } installed)
            {
                await LogVersion(installed, cancellationToken);
                return installed;
            }
        }

        throw new InvalidOperationException($"Node.js {minVersion} not found.");
    }

    private async ValueTask LogVersion(string nodePath, CancellationToken cancellationToken)
    {
        string? version = await GetVersionAtPath(nodePath, cancellationToken).NoSync();
        if (!string.IsNullOrWhiteSpace(version))
            _logger.LogInformation("Node.js found at {Path}, version {Version}.", nodePath, version);
    }

    private async ValueTask<string?> GetVersionAtPath(string nodePath, CancellationToken ct)
    {
        try
        {
            string output = await _processUtil.StartAndGetOutput(
                nodePath,
                _scriptVersion,
                "",
                _probeTimeout,
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

        if (OperatingSystem.IsWindows())
        {
            if (await ProbeHostedToolCacheAsync(required!, cancellationToken).NoSync() is { } cached)
                return cached;
        }

        string[] commands = OperatingSystem.IsWindows() ? _nodeCommandsWindows : _nodeCommandsUnix;

        for (int i = 0; i < commands.Length; i++)
        {
            if (await Probe(commands[i], required!, cancellationToken).NoSync() is { } found)
                return found;
        }

        return null;
    }

    public async ValueTask<string?> TryLocateAny(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            if (await ProbeHostedToolCacheAnyAsync(cancellationToken).NoSync() is { } cached)
                return cached;
        }

        string[] commands = OperatingSystem.IsWindows() ? _nodeCommandsWindows : _nodeCommandsUnix;

        for (int i = 0; i < commands.Length; i++)
        {
            if (await ProbeAny(commands[i], cancellationToken).NoSync() is { } found)
                return found;
        }

        return null;
    }

    public async ValueTask TryInstall(Version? version, CancellationToken cancellationToken = default)
    {
        bool latest = version is null;
        int major = version?.Major ?? 0;
        string ver = major.ToString();

        if (OperatingSystem.IsLinux())
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
        else if (OperatingSystem.IsWindows())
        {
            string wingetId = latest ? "OpenJS.NodeJS" : $"OpenJS.NodeJS.{major}";
            string wingetArgs = latest
                ? "install --exact --id OpenJS.NodeJS --silent --disable-interactivity --accept-source-agreements --accept-package-agreements --source winget"
                : $"install --exact --id {wingetId} --silent --disable-interactivity --accept-source-agreements --accept-package-agreements --source winget";

            if (await _processUtil.CommandExistsAndRuns("winget", "--version", _existsTimeout, cancellationToken).NoSync())
            {
                try
                {
                    await _processUtil.StartAndGetOutput(
                        "winget",
                        wingetArgs,
                        "",
                        _installTimeoutWin,
                        cancellationToken
                    ).NoSync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "winget install {WingetId} failed (node may already be installed or install may require elevation).", wingetId);
                }
            }
            else if (await _processUtil.CommandExistsAndRuns("choco", "--version", _existsTimeout, cancellationToken).NoSync())
            {
                try
                {
                    string chocoArgs = latest
                        ? "install nodejs -y --no-progress"
                        : $"install nodejs --version {major}.0.0 -y --no-progress";

                    await _processUtil.StartAndGetOutput(
                        "choco",
                        chocoArgs,
                        "",
                        _installTimeoutWin,
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
        else if (OperatingSystem.IsMacOS())
        {
            try
            {
                string brewArgs = latest ? "install node" : $"install node@{ver}";
                await _processUtil.StartAndGetOutput(
                    "brew",
                    brewArgs,
                    "",
                    _installTimeoutMac,
                    cancellationToken
                ).NoSync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "brew install node failed (node may already be installed).");
            }
        }
    }

    private async ValueTask<string?> ProbeHostedToolCacheAsync(Version target, CancellationToken cancellationToken)
    {
        string root = Environment.GetEnvironmentVariable("AGENT_TOOLSDIRECTORY") ?? @"C:\hostedtoolcache\windows";
        string nodeRoot = Path.Combine(root, "Node");
        if (!(await _directoryUtil.Exists(nodeRoot, cancellationToken)))
            return null;

        List<string> verDirs = await _directoryUtil.GetAllDirectories(nodeRoot, cancellationToken);
        foreach (string verDir in verDirs)
        {
            string? dirName = Path.GetFileName(verDir);
            if (string.IsNullOrEmpty(dirName))
                continue;

            if (!Version.TryParse(dirName, out Version? v) || !MatchMajorMinor(v, target))
                continue;

            string candidate = Path.Combine(verDir, "x64", "node.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private async ValueTask<string?> ProbeHostedToolCacheAnyAsync(CancellationToken cancellationToken)
    {
        string root = Environment.GetEnvironmentVariable("AGENT_TOOLSDIRECTORY") ?? @"C:\hostedtoolcache\windows";
        string nodeRoot = Path.Combine(root, "Node");
        if (!(await _directoryUtil.Exists(nodeRoot, cancellationToken)))
            return null;

        List<string> verDirs = await _directoryUtil.GetAllDirectories(nodeRoot, cancellationToken);
        foreach (string verDir in verDirs)
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
                _scriptExecPath,
                "",
                _probeTimeout,
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
                _scriptExecPathAndVersion,
                "",
                _probeTimeout,
                ct
            ).NoSync();

            ReadOnlySpan<char> s = output.AsSpan();
            s = s.Trim();

            // Find last newline (supports both \n and \r\n)
            int nl = s.LastIndexOf('\n');
            if (nl <= 0)
                return null;

            ReadOnlySpan<char> execPathSpan = s[..nl].Trim();
            ReadOnlySpan<char> versionSpan = s[(nl + 1)..].Trim();

            if (execPathSpan.IsEmpty || versionSpan.IsEmpty)
                return null;

            if (versionSpan[0] == 'v')
                versionSpan = versionSpan[1..].Trim();

            // Version.TryParse requires string
            if (!Version.TryParse(versionSpan.ToString(), out Version? v))
                return null;

            if (!MatchMajorMinor(v, target))
                return null;

            return execPathSpan.ToString();
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

        ReadOnlySpan<char> s = version.AsSpan().Trim();
        if (!s.IsEmpty && s[0] == 'v')
            s = s[1..].Trim();

        // Normalize "20" -> "20.0" to satisfy Version.TryParse expectations
        if (s.IndexOf('.') < 0)
            return Version.TryParse($"{s}.0", out result);

        return Version.TryParse(s.ToString(), out result);
    }
}
