using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Soenneker.Extensions.ValueTask;
using Soenneker.Node.Util.Abstract;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.Process.Abstract;
using Soenneker.Extensions.String;

namespace Soenneker.Node.Util;

/// <inheritdoc cref="INodeUtil"/>
public sealed class NodeUtil : INodeUtil
{
    private static readonly TimeSpan _probeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _existsTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan _installTimeoutWin = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _installTimeoutMac = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan _npmInstallTimeoutWin = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan _npmInstallTimeoutUnix = TimeSpan.FromMinutes(20);

    private const string _scriptExecPath = "-e \"console.log(process.execPath)\"";
    private const string _scriptVersion = "-e \"console.log(process.version)\"";
    private const string _scriptExecPathAndVersion = "-e \"console.log(process.execPath + '\\n' + process.version)\"";

    private static readonly string[] _nodeCommandsWindows = ["node", "node.exe"];
    private static readonly string[] _nodeCommandsUnix = ["node", "nodejs"];

    private static readonly string[] _npxNamesWindows = ["npx.cmd", "npx.exe", "npx"];
    private static readonly string[] _npxNamesUnix = ["npx"];

    private static readonly string[] _npmNamesWindows = ["npm.cmd", "npm.exe", "npm"];
    private static readonly string[] _npmNamesUnix = ["npm"];

    private const string _macNpx1 = "/usr/local/bin/npx";
    private const string _macNpx2 = "/opt/homebrew/bin/npx";
    private const string _linuxNpx1 = "/usr/bin/npx";
    private const string _linuxNpx2 = "/usr/local/bin/npx";

    private const string _macNpm1 = "/usr/local/bin/npm";
    private const string _macNpm2 = "/opt/homebrew/bin/npm";
    private const string _linuxNpm1 = "/usr/bin/npm";
    private const string _linuxNpm2 = "/usr/local/bin/npm";

    private readonly IProcessUtil _processUtil;
    private readonly ILogger<NodeUtil> _logger;
    private readonly IDirectoryUtil _directoryUtil;
    private readonly IFileUtil _fileUtil;

    public NodeUtil(IProcessUtil processUtil, ILogger<NodeUtil> logger, IDirectoryUtil directoryUtil, IFileUtil fileUtil)
    {
        _processUtil = processUtil;
        _logger = logger;
        _directoryUtil = directoryUtil;
        _fileUtil = fileUtil;
    }

    public ValueTask<string> GetNpxPath(CancellationToken cancellationToken = default)
    {
        string[] names = OperatingSystem.IsWindows() ? _npxNamesWindows : _npxNamesUnix;

        return ResolveExecutable(
            names,
            defaultCommand: "npx",
            windowsProbe: ProbeWindowsNpx,
            macProbe: ProbeMacNpx,
            linuxProbe: ProbeLinuxNpx,
            cancellationToken);
    }

    public ValueTask<string> GetNpmPath(CancellationToken cancellationToken = default)
    {
        string[] names = OperatingSystem.IsWindows() ? _npmNamesWindows : _npmNamesUnix;

        return ResolveExecutable(
            names,
            defaultCommand: "npm",
            windowsProbe: ProbeWindowsNpm,
            macProbe: ProbeMacNpm,
            linuxProbe: ProbeLinuxNpm,
            cancellationToken);
    }

    private async ValueTask<string> ResolveExecutable(
        string[] names,
        string defaultCommand,
        Func<CancellationToken, ValueTask<string?>> windowsProbe,
        Func<CancellationToken, ValueTask<string?>> macProbe,
        Func<CancellationToken, ValueTask<string?>> linuxProbe,
        CancellationToken cancellationToken)
    {
        if (await TryResolveFromPathEnv(names, cancellationToken).NoSync() is { } fromPath)
            return fromPath;

        string? osFound = null;

        if (OperatingSystem.IsWindows())
            osFound = await windowsProbe(cancellationToken).NoSync();
        else if (OperatingSystem.IsMacOS())
            osFound = await macProbe(cancellationToken).NoSync();
        else if (OperatingSystem.IsLinux())
            osFound = await linuxProbe(cancellationToken).NoSync();

        return osFound ?? defaultCommand;
    }


    private async ValueTask<string?> TryResolveFromPathEnv(string[] names, CancellationToken cancellationToken)
    {
        try
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv))
                return null;

            char sep = Path.PathSeparator;

            int pos = 0;

            while (pos <= pathEnv.Length)
            {
                int next = pathEnv.IndexOf(sep, pos);
                int end = next >= 0 ? next : pathEnv.Length;

                // Extract segment [pos, end)
                int len = end - pos;

                // Advance cursor NOW (so only ints live across awaits)
                pos = next >= 0 ? end + 1 : pathEnv.Length + 1;

                if (len <= 0)
                    continue;

                // Span is used only inside this block and is gone before any await
                ReadOnlySpan<char> dirSpan = pathEnv.AsSpan(end - len, len).Trim();
                if (dirSpan.IsEmpty)
                    continue;

                string dir = dirSpan.ToString();

                for (int i = 0; i < names.Length; i++)
                {
                    string full = Path.Combine(dir, names[i]);

                    if (await _fileUtil.Exists(full, cancellationToken).NoSync())
                        return full;
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private async ValueTask<string?> ProbeMacNpx(CancellationToken cancellationToken)
    {
        if (await _fileUtil.Exists(_macNpx1, cancellationToken).NoSync()) return _macNpx1;
        if (await _fileUtil.Exists(_macNpx2, cancellationToken).NoSync()) return _macNpx2;
        return null;
    }

    private async ValueTask<string?> ProbeLinuxNpx(CancellationToken cancellationToken)
    {
        if (await _fileUtil.Exists(_linuxNpx1, cancellationToken).NoSync()) return _linuxNpx1;
        if (await _fileUtil.Exists(_linuxNpx2, cancellationToken).NoSync()) return _linuxNpx2;
        return null;
    }

    private async ValueTask<string?> ProbeMacNpm(CancellationToken cancellationToken)
    {
        if (await _fileUtil.Exists(_macNpm1, cancellationToken).NoSync()) return _macNpm1;
        if (await _fileUtil.Exists(_macNpm2, cancellationToken).NoSync()) return _macNpm2;
        return null;
    }

    private async ValueTask<string?> ProbeLinuxNpm(CancellationToken cancellationToken)
    {
        if (await _fileUtil.Exists(_linuxNpm1, cancellationToken).NoSync()) return _linuxNpm1;
        if (await _fileUtil.Exists(_linuxNpm2, cancellationToken).NoSync()) return _linuxNpm2;
        return null;
    }

    private async ValueTask<string?> ProbeWindowsNpx(CancellationToken cancellationToken)
    {
        // Probe common Windows locations without building a list

        string? programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        if (programFiles.HasContent())
        {
            string c1 = Path.Combine(programFiles, "nodejs", "npx.cmd");
            if (await _fileUtil.Exists(c1, cancellationToken).NoSync()) return c1;

            string c2 = Path.Combine(programFiles, "nodejs", "npx.exe");
            if (await _fileUtil.Exists(c2, cancellationToken).NoSync()) return c2;
        }

        string? localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (localAppData.HasContent())
        {
            string c1 = Path.Combine(localAppData, "Programs", "node", "npx.cmd");
            if (await _fileUtil.Exists(c1, cancellationToken).NoSync()) return c1;

            string c2 = Path.Combine(localAppData, "Programs", "node", "npx.exe");
            if (await _fileUtil.Exists(c2, cancellationToken).NoSync()) return c2;
        }

        string? appData = Environment.GetEnvironmentVariable("APPDATA");
        if (appData.HasContent())
        {
            string c = Path.Combine(appData, "npm", "npx.cmd");
            if (await _fileUtil.Exists(c, cancellationToken).NoSync()) return c;
        }

        return null;
    }

    private async ValueTask<string?> ProbeWindowsNpm(CancellationToken cancellationToken)
    {
        string? programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        if (programFiles.HasContent())
        {
            string c1 = Path.Combine(programFiles, "nodejs", "npm.cmd");
            if (await _fileUtil.Exists(c1, cancellationToken).NoSync()) return c1;

            string c2 = Path.Combine(programFiles, "nodejs", "npm.exe");
            if (await _fileUtil.Exists(c2, cancellationToken).NoSync()) return c2;
        }

        string? localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (localAppData.HasContent())
        {
            string c1 = Path.Combine(localAppData, "Programs", "node", "npm.cmd");
            if (await _fileUtil.Exists(c1, cancellationToken).NoSync()) return c1;

            string c2 = Path.Combine(localAppData, "Programs", "node", "npm.exe");
            if (await _fileUtil.Exists(c2, cancellationToken).NoSync()) return c2;
        }

        string? appData = Environment.GetEnvironmentVariable("APPDATA");
        if (appData.HasContent())
        {
            string c = Path.Combine(appData, "npm", "npm.cmd");
            if (await _fileUtil.Exists(c, cancellationToken).NoSync()) return c;
        }

        return null;
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

        if (version.HasContent())
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
        if (minVersion.IsNullOrWhiteSpace())
            return await TryLocateAny(cancellationToken).NoSync();

        if (!TryParseVersion(minVersion, out Version? required))
            return null;

        if (OperatingSystem.IsWindows())
        {
            if (await ProbeHostedToolCache(required!, cancellationToken).NoSync() is { } cached)
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
            if (await ProbeHostedToolCacheAny(cancellationToken).NoSync() is { } cached)
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

    public async ValueTask<string> NpmInstall(
        string directory,
        bool cleanInstall = false,          // true => npm ci, false => npm install
        bool omitDevDependencies = false,   // adds --omit=dev
        bool ignoreScripts = false,         // adds --ignore-scripts
        bool noAudit = true,                // adds --no-audit (default true)
        bool noFund = true,                 // adds --no-fund (default true)
        CancellationToken cancellationToken = default)
    {
        if (directory.IsNullOrWhiteSpace())
            throw new ArgumentException("Directory is required.", nameof(directory));

        directory = Path.GetFullPath(directory);

        if (!await _directoryUtil.Exists(directory, cancellationToken).NoSync())
            throw new DirectoryNotFoundException($"Directory not found: {directory}");

        // Ensure node is present (npm should come with it)
        await EnsureInstalled(null, installIfMissing: true, cancellationToken).NoSync();

        string packageJson = Path.Combine(directory, "package.json");

        if (!await _fileUtil.Exists(packageJson, cancellationToken).NoSync())
            _logger.LogWarning("npm install requested but package.json not found in {Directory}.", directory);

        string npm = await GetNpmPath(cancellationToken).NoSync();

        string args = cleanInstall ? "ci" : "install";

        if (omitDevDependencies)
            args += " --omit=dev";

        if (ignoreScripts)
            args += " --ignore-scripts";

        if (noAudit)
            args += " --no-audit";

        if (noFund)
            args += " --no-fund";

        TimeSpan timeout = OperatingSystem.IsWindows() ? _npmInstallTimeoutWin : _npmInstallTimeoutUnix;

        _logger.LogInformation("Running {Cmd} {Args} in {Directory}", npm, args, directory);

        // working directory = target directory
        string output = await _processUtil.StartAndGetOutput(
            npm,
            args,
            directory,
            timeout,
            cancellationToken
        ).NoSync();

        return output;
    }

    private async ValueTask<string?> ProbeHostedToolCache(Version target, CancellationToken cancellationToken)
    {
        string root = Environment.GetEnvironmentVariable("AGENT_TOOLSDIRECTORY") ?? @"C:\hostedtoolcache\windows";
        string nodeRoot = Path.Combine(root, "Node");

        if (!await _directoryUtil.Exists(nodeRoot, cancellationToken).NoSync())
            return null;

        List<string> verDirs = await _directoryUtil.GetAllDirectories(nodeRoot, cancellationToken).NoSync();

        foreach (string verDir in verDirs)
        {
            string? dirName = Path.GetFileName(verDir);
            if (string.IsNullOrEmpty(dirName))
                continue;

            if (!Version.TryParse(dirName, out Version? v) || !MatchMajorMinor(v, target))
                continue;

            string candidate = Path.Combine(verDir, "x64", "node.exe");

            if (await _fileUtil.Exists(candidate, cancellationToken).NoSync())
                return candidate;
        }

        return null;
    }

    private async ValueTask<string?> ProbeHostedToolCacheAny(CancellationToken cancellationToken)
    {
        string root = Environment.GetEnvironmentVariable("AGENT_TOOLSDIRECTORY") ?? @"C:\hostedtoolcache\windows";
        string nodeRoot = Path.Combine(root, "Node");

        if (!await _directoryUtil.Exists(nodeRoot, cancellationToken).NoSync())
            return null;

        List<string> verDirs = await _directoryUtil.GetAllDirectories(nodeRoot, cancellationToken).NoSync();

        foreach (string verDir in verDirs)
        {
            string candidate = Path.Combine(verDir, "x64", "node.exe");

            if (await _fileUtil.Exists(candidate, cancellationToken).NoSync())
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

            int nl = s.LastIndexOf('\n');
            if (nl <= 0)
                return null;

            ReadOnlySpan<char> execPathSpan = s[..nl].Trim();
            ReadOnlySpan<char> versionSpan = s[(nl + 1)..].Trim();

            if (execPathSpan.IsEmpty || versionSpan.IsEmpty)
                return null;

            if (versionSpan[0] == 'v')
                versionSpan = versionSpan[1..].Trim();

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

        if (s.IndexOf('.') < 0)
            return Version.TryParse($"{s}.0", out result);

        return Version.TryParse(s.ToString(), out result);
    }
}
