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
using Soenneker.Utils.Runtime;

namespace Soenneker.Node.Util;

/// <inheritdoc cref="INodeUtil"/>
public sealed partial class NodeUtil : INodeUtil
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

    private static readonly string[] _npmNamesWindows = ["npm.cmd", "npm.exe", "npm"];
    private static readonly string[] _npmNamesUnix = ["npm"];

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

    /// <summary>
    /// Gets npx path.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing the result of the operation.</returns>
    public async ValueTask<string> GetNpxPath(CancellationToken cancellationToken = default)
    {
        string npmPath = await GetNpmPath(cancellationToken).NoSync();

        string? dir = Path.GetDirectoryName(npmPath);

        if (dir == null)
            return "npx";

        string fileName = RuntimeUtil.IsWindows() ? "npx.cmd" : "npx";
        string candidate = Path.Combine(dir, fileName);

        if (await _fileUtil.Exists(candidate, cancellationToken).NoSync())
            return candidate;

        // fallback to PATH
        return "npx";
    }

    /// <summary>
    /// Gets npm path.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing the result of the operation.</returns>
    public ValueTask<string> GetNpmPath(CancellationToken cancellationToken = default)
    {
        string[] names = RuntimeUtil.IsWindows() ? _npmNamesWindows : _npmNamesUnix;

        return ResolveExecutable(names, defaultCommand: "npm", windowsProbe: ProbeWindowsNpm, macProbe: ProbeMacNpm, linuxProbe: ProbeLinuxNpm,
            cancellationToken);
    }

    private async ValueTask<string> ResolveExecutable(string[] names, string defaultCommand, Func<CancellationToken, ValueTask<string?>> windowsProbe,
        Func<CancellationToken, ValueTask<string?>> macProbe, Func<CancellationToken, ValueTask<string?>> linuxProbe, CancellationToken cancellationToken)
    {
        if (await TryResolveFromPathEnv(names, cancellationToken)
                .NoSync() is { } fromPath)
            return fromPath;

        string? osFound = null;

        if (RuntimeUtil.IsWindows())
            osFound = await windowsProbe(cancellationToken)
                .NoSync();
        else if (RuntimeUtil.IsMacOs())
            osFound = await macProbe(cancellationToken)
                .NoSync();
        else if (RuntimeUtil.IsLinux())
            osFound = await linuxProbe(cancellationToken)
                .NoSync();

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

            var pos = 0;

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
                ReadOnlySpan<char> dirSpan = pathEnv.AsSpan(end - len, len)
                                                    .Trim();
                if (dirSpan.IsEmpty)
                    continue;

                var dir = dirSpan.ToString();

                for (var i = 0; i < names.Length; i++)
                {
                    string full = Path.Combine(dir, names[i]);

                    if (await _fileUtil.Exists(full, cancellationToken)
                                       .NoSync())
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

    private async ValueTask<string?> ProbeMacNpm(CancellationToken cancellationToken)
    {
        if (await _fileUtil.Exists(_macNpm1, cancellationToken)
                           .NoSync())
            return _macNpm1;
        if (await _fileUtil.Exists(_macNpm2, cancellationToken)
                           .NoSync())
            return _macNpm2;
        return null;
    }

    private async ValueTask<string?> ProbeLinuxNpm(CancellationToken cancellationToken)
    {
        if (await _fileUtil.Exists(_linuxNpm1, cancellationToken)
                           .NoSync())
            return _linuxNpm1;
        if (await _fileUtil.Exists(_linuxNpm2, cancellationToken)
                           .NoSync())
            return _linuxNpm2;
        return null;
    }

    private async ValueTask<string?> ProbeWindowsNpm(CancellationToken cancellationToken)
    {
        string? programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        if (programFiles.HasContent())
        {
            string c1 = Path.Combine(programFiles, "nodejs", "npm.cmd");
            if (await _fileUtil.Exists(c1, cancellationToken)
                               .NoSync())
                return c1;

            string c2 = Path.Combine(programFiles, "nodejs", "npm.exe");
            if (await _fileUtil.Exists(c2, cancellationToken)
                               .NoSync())
                return c2;
        }

        string? localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (localAppData.HasContent())
        {
            string c1 = Path.Combine(localAppData, "Programs", "node", "npm.cmd");
            if (await _fileUtil.Exists(c1, cancellationToken)
                               .NoSync())
                return c1;

            string c2 = Path.Combine(localAppData, "Programs", "node", "npm.exe");
            if (await _fileUtil.Exists(c2, cancellationToken)
                               .NoSync())
                return c2;
        }

        string? appData = Environment.GetEnvironmentVariable("APPDATA");
        if (appData.HasContent())
        {
            string c = Path.Combine(appData, "npm", "npm.cmd");
            if (await _fileUtil.Exists(c, cancellationToken)
                               .NoSync())
                return c;
        }

        return null;
    }

    /// <summary>
    /// Gets node path.
    /// </summary>
    /// <param name="nodeCommand">The node command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing the result of the operation.</returns>
    public async ValueTask<string> GetNodePath(string nodeCommand = "node", CancellationToken cancellationToken = default)
    {
        string result = await _processUtil.StartAndGetOutput(nodeCommand, _scriptExecPath, "", _probeTimeout, cancellationToken)
                                          .NoSync();

        return result.Trim();
    }

    private async ValueTask LogVersion(string nodePath, CancellationToken cancellationToken)
    {
        string? version = await GetVersionAtPath(nodePath, cancellationToken)
            .NoSync();

        if (version.HasContent())
            _logger.LogInformation("Node.js found at {Path}, version {Version}.", nodePath, version);
    }

    private async ValueTask<string?> GetVersionAtPath(string nodePath, CancellationToken ct)
    {
        try
        {
            string output = await _processUtil.StartAndGetOutput(nodePath, _scriptVersion, "", _probeTimeout, ct)
                                              .NoSync();

            return output.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Attempts to execute locate.
    /// </summary>
    /// <param name="minVersion">The min version.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing the result of the operation.</returns>
    public async ValueTask<string?> TryLocate(string? minVersion = null, CancellationToken cancellationToken = default)
    {
        if (minVersion.IsNullOrWhiteSpace())
            return await TryLocateAny(cancellationToken)
                .NoSync();

        if (!TryParseVersion(minVersion, out Version? required))
            return null;

        if (OperatingSystem.IsWindows())
        {
            if (await ProbeHostedToolCache(required!, cancellationToken)
                    .NoSync() is { } cached)
                return cached;
        }

        string[] commands = OperatingSystem.IsWindows() ? _nodeCommandsWindows : _nodeCommandsUnix;

        for (var i = 0; i < commands.Length; i++)
        {
            if (await Probe(commands[i], required!, cancellationToken)
                    .NoSync() is { } found)
                return found;
        }

        return null;
    }

    /// <summary>
    /// Attempts to execute locate any.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing the result of the operation.</returns>
    public async ValueTask<string?> TryLocateAny(CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            if (await ProbeHostedToolCacheAny(cancellationToken)
                    .NoSync() is { } cached)
                return cached;
        }

        string[] commands = OperatingSystem.IsWindows() ? _nodeCommandsWindows : _nodeCommandsUnix;

        for (var i = 0; i < commands.Length; i++)
        {
            if (await ProbeAny(commands[i], cancellationToken)
                    .NoSync() is { } found)
                return found;
        }

        return null;
    }

    private async ValueTask<string?> ProbeHostedToolCache(Version target, CancellationToken cancellationToken)
    {
        string root = Environment.GetEnvironmentVariable("AGENT_TOOLSDIRECTORY") ?? @"C:\hostedtoolcache\windows";
        string nodeRoot = Path.Combine(root, "Node");

        if (!await _directoryUtil.Exists(nodeRoot, cancellationToken)
                                 .NoSync())
            return null;

        List<string> verDirs = await _directoryUtil.GetAllDirectories(nodeRoot, cancellationToken)
                                                   .NoSync();

        foreach (string verDir in verDirs)
        {
            string? dirName = Path.GetFileName(verDir);
            if (string.IsNullOrEmpty(dirName))
                continue;

            if (!Version.TryParse(dirName, out Version? v) || !MatchMajorMinor(v, target))
                continue;

            string candidate = Path.Combine(verDir, "x64", "node.exe");

            if (await _fileUtil.Exists(candidate, cancellationToken)
                               .NoSync())
                return candidate;
        }

        return null;
    }

    private async ValueTask<string?> ProbeHostedToolCacheAny(CancellationToken cancellationToken)
    {
        string root = Environment.GetEnvironmentVariable("AGENT_TOOLSDIRECTORY") ?? @"C:\hostedtoolcache\windows";
        string nodeRoot = Path.Combine(root, "Node");

        if (!await _directoryUtil.Exists(nodeRoot, cancellationToken)
                                 .NoSync())
            return null;

        List<string> verDirs = await _directoryUtil.GetAllDirectories(nodeRoot, cancellationToken)
                                                   .NoSync();

        foreach (string verDir in verDirs)
        {
            string candidate = Path.Combine(verDir, "x64", "node.exe");

            if (await _fileUtil.Exists(candidate, cancellationToken)
                               .NoSync())
                return candidate;
        }

        return null;
    }

    private async ValueTask<string?> ProbeAny(string command, CancellationToken ct)
    {
        try
        {
            string output = await _processUtil.StartAndGetOutput(command, _scriptExecPath, "", _probeTimeout, ct)
                                              .NoSync();

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
            string output = await _processUtil.StartAndGetOutput(command, _scriptExecPathAndVersion, "", _probeTimeout, ct)
                                              .NoSync();

            ReadOnlySpan<char> s = output.AsSpan();
            s = s.Trim();

            int nl = s.LastIndexOf('\n');
            if (nl <= 0)
                return null;

            ReadOnlySpan<char> execPathSpan = s[..nl]
                .Trim();
            ReadOnlySpan<char> versionSpan = s[(nl + 1)..]
                .Trim();

            if (execPathSpan.IsEmpty || versionSpan.IsEmpty)
                return null;

            if (versionSpan[0] == 'v')
                versionSpan = versionSpan[1..]
                    .Trim();

            if (!Version.TryParse(versionSpan, out Version? v))
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

    /// <summary>
    /// Gets pnpm path.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing the result of the operation.</returns>
    public ValueTask<string> GetPnpmPath(CancellationToken cancellationToken = default)
    {
        return GetGlobalToolPath("pnpm", cancellationToken);
    }

    /// <summary>
    /// Gets npm global bin directory.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing the result of the operation.</returns>
    public async ValueTask<string> GetNpmGlobalBinDirectory(CancellationToken cancellationToken = default)
    {
        string npmPath = await GetNpmPath(cancellationToken).NoSync();

        string output = await _processUtil.StartAndGetOutput(
            npmPath,
            "prefix -g",
            "",
            _probeTimeout,
            cancellationToken).NoSync();

        string prefix = output.Trim();

        if (prefix.IsNullOrWhiteSpace())
            throw new InvalidOperationException("Could not resolve npm global prefix.");

        return RuntimeUtil.IsWindows()
            ? prefix
            : Path.Combine(prefix, "bin");
    }

    /// <summary>
    /// Gets global tool path.
    /// </summary>
    /// <param name="toolName">The tool name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing the result of the operation.</returns>
    public async ValueTask<string> GetGlobalToolPath(string toolName, CancellationToken cancellationToken = default)
    {
        string directory = await GetNpmGlobalBinDirectory(cancellationToken)
            .NoSync();

        string fileName = RuntimeUtil.IsWindows() ? $"{toolName}.cmd" : toolName;
        string path = Path.Combine(directory, fileName);

        if (!await _fileUtil.Exists(path, cancellationToken)
                            .NoSync())
            throw new FileNotFoundException($"Global tool was not found at expected path: {path}", path);

        return path;
    }

    private static bool MatchMajorMinor(Version found, Version target) =>
        found.Major == target.Major && (target.Minor < 1 || found.Minor == target.Minor);

    private static bool TryParseVersion(string version, out Version? result)
    {
        result = null;

        if (version.IsNullOrWhiteSpace())
            return false;

        ReadOnlySpan<char> s = version.AsSpan()
                                      .Trim();
        if (!s.IsEmpty && s[0] == 'v')
            s = s[1..]
                .Trim();

        if (s.IndexOf('.') < 0)
        {
            if (!int.TryParse(s, out int major) || major < 0)
                return false;

            result = new Version(major, 0);
            return true;
        }

        return Version.TryParse(s, out result);
    }
}
