using Microsoft.Extensions.Logging;
using Soenneker.Extensions.String;
using Soenneker.Extensions.ValueTask;
using Soenneker.Node.Util.Abstract;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Extensions.Task;
using Soenneker.Hashing.XxHash;

namespace Soenneker.Node.Util;

/// <inheritdoc cref="INodeUtil"/>
public sealed partial class NodeUtil : INodeUtil
{
    private const string _npmMarkerFileName = "npm-install.lockhash";
    
    private static string GetMarkerPath(string directory) =>
        Path.Combine(directory, _npmMarkerFileName);

    private static string GetNodeModulesPath(string directory) =>
        Path.Combine(directory, "node_modules");

    private static string GetPackageLockPath(string directory) =>
        Path.Combine(directory, "package-lock.json");

    private static string GetShrinkwrapPath(string directory) =>
        Path.Combine(directory, "npm-shrinkwrap.json");

    private static string GetPackageJsonPath(string directory) =>
        Path.Combine(directory, "package.json");

    public async ValueTask<string> EnsureInstalled(string? minVersion = null, bool installIfMissing = true, CancellationToken cancellationToken = default)
    {
        bool anyVersion = minVersion.IsNullOrWhiteSpace();

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
        bool skipIfUpToDate = true,         // <--- NEW
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

        if (skipIfUpToDate)
        {
            if (await IsNpmInstallUpToDate(directory, cleanInstall, cancellationToken).NoSync())
            {
                _logger.LogInformation("Skipping npm install in {Directory} (node_modules up-to-date).", directory);
                return string.Empty;
            }
        }

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

        // Always write lockhash after npm install
        await WriteNpmInstallMarkerIfPossible(directory, cancellationToken).NoSync();

        return output;
    }

    private async ValueTask<bool> IsNpmInstallUpToDate(string directory, bool cleanInstall, CancellationToken ct)
    {
        // Must have node_modules
        if (!await _directoryUtil.Exists(GetNodeModulesPath(directory), ct).NoSync())
            return false;

        // Must have marker
        string markerPath = GetMarkerPath(directory);

        if (!await _fileUtil.Exists(markerPath, ct).NoSync())
            return false;

        // Determine input file for hashing (prefer shrinkwrap > lock > package.json)
        string? hashInput = await GetBestHashInputFile(directory, ct).NoSync();
        if (hashInput is null)
            return false;

        // If user requested `npm ci`, require a lock/shrinkwrap (package.json alone isn't deterministic enough)
        if (cleanInstall && hashInput.EndsWith("package.json", StringComparison.OrdinalIgnoreCase))
            return false;

        string current = await ComputeHash(hashInput, ct).NoSync();

        string stored;

        try
        {
            stored = (await _fileUtil.Read(markerPath, false, cancellationToken: ct).NoSync()).Trim();
        }
        catch
        {
            return false;
        }

        if (stored.Length == 0)
            return false;

        return string.Equals(stored, current, StringComparison.Ordinal);
    }

    private async ValueTask WriteNpmInstallMarker(string directory, CancellationToken ct)
    {
        string? hashInput = await GetBestHashInputFile(directory, ct).NoSync();
        if (hashInput is null)
            return;

        string hash = await ComputeHash(hashInput, ct).NoSync();

        string markerPath = GetMarkerPath(directory);

        if (await _fileUtil.Exists(markerPath, ct).NoSync())
        {
            string existing = (await _fileUtil.Read(markerPath, false, cancellationToken: ct).NoSync()).Trim();
            if (string.Equals(existing, hash, StringComparison.Ordinal))
                return;
        }

        await _fileUtil.Write(markerPath, hash, true, ct).NoSync();
    }

    /// <summary>
    /// Writes the npm-install lockhash marker when possible. Swallows errors so marker failures don't fail the build.
    /// </summary>
    private async ValueTask WriteNpmInstallMarkerIfPossible(string directory, CancellationToken ct)
    {
        try
        {
            await WriteNpmInstallMarker(directory, ct).NoSync();
        }
        catch
        {
            // marker failures shouldn't fail the build
        }
    }

    private async ValueTask<string?> GetBestHashInputFile(string directory, CancellationToken ct)
    {
        string shrinkwrap = GetShrinkwrapPath(directory);

        if (await _fileUtil.Exists(shrinkwrap, ct).NoSync())
            return shrinkwrap;

        string lockFile = GetPackageLockPath(directory);

        if (await _fileUtil.Exists(lockFile, ct).NoSync())
            return lockFile;

        string packageJson = GetPackageJsonPath(directory);

        if (await _fileUtil.Exists(packageJson, ct).NoSync())
            return packageJson;

        return null;
    }

    private async ValueTask<string> ComputeHash(string filePath, CancellationToken ct)
    {
        string value = await _fileUtil.Read(filePath, false, ct).NoSync();

        string result = XxHash3Util.Hash(value);

        return result;
    }
}
