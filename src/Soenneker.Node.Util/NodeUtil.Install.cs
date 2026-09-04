using Microsoft.Extensions.Logging;
using Soenneker.Extensions.String;
using Soenneker.Extensions.ValueTask;
using Soenneker.Asyncs.Locks;
using Soenneker.Dictionaries.SingletonKeys;
using Soenneker.Node.Util.Abstract;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Extensions.Task;
using Soenneker.Hashing.XxHash;
using Soenneker.Utils.Runtime;

namespace Soenneker.Node.Util;

public sealed partial class NodeUtil
{
    private const string _npmMarkerFileName = "npm-install.lockhash";
    private static readonly SingletonKeyDictionary<string, AsyncLock> _npmInstallLocks = new(static _ => new AsyncLock());
    
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

    private static string GetNpmInstallLockKey(string directory)
    {
        directory = Path.TrimEndingDirectorySeparator(directory);

        return OperatingSystem.IsWindows() ? directory.ToUpperInvariant() : directory;
    }

    /// <summary>
    /// Executes the ensure installed operation.
    /// </summary>
    /// <param name="minVersion">The min version.</param>
    /// <param name="installIfMissing">The install if missing.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing the result of the operation.</returns>
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

    /// <summary>
    /// Attempts to execute install.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask TryInstall(Version? version, CancellationToken cancellationToken = default)
    {
        bool latest = version is null;
        int major = version?.Major ?? 0;
        var ver = major.ToString();

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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
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
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
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
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "brew install node failed (node may already be installed).");
            }
        }
    }

    /// <summary>
    /// Executes the npm install operation.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="cleanInstall">The clean install.</param>
    /// <param name="omitDevDependencies">The omit dev dependencies.</param>
    /// <param name="ignoreScripts">The ignore scripts.</param>
    /// <param name="noAudit">The no audit.</param>
    /// <param name="noFund">The no fund.</param>
    /// <param name="skipIfUpToDate">The skip if up to date.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing the result of the operation.</returns>
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

        AsyncLock installLock = await _npmInstallLocks.Get(GetNpmInstallLockKey(directory), cancellationToken).NoSync();

        using (await installLock.Lock(cancellationToken).ConfigureAwait(false))
        {
            return await NpmInstallUnderLock(directory, cleanInstall, omitDevDependencies, ignoreScripts, noAudit, noFund, skipIfUpToDate,
                cancellationToken).NoSync();
        }
    }

    private async ValueTask<string> NpmInstallUnderLock(string directory, bool cleanInstall, bool omitDevDependencies, bool ignoreScripts, bool noAudit,
        bool noFund, bool skipIfUpToDate, CancellationToken cancellationToken)
    {

        string nodePath = await EnsureInstalled(null, installIfMissing: true, cancellationToken).NoSync();
        string npm = await GetNpmPath(cancellationToken).NoSync();
        string? nodeVersion = await GetVersionAtPath(nodePath, cancellationToken).NoSync();
        string npmVersion = (await _processUtil.StartAndGetOutput(npm, "--version", directory, _probeTimeout, cancellationToken).NoSync()).Trim();

        string packageJson = Path.Combine(directory, "package.json");

        if (!await _fileUtil.Exists(packageJson, cancellationToken).NoSync())
            _logger.LogWarning("npm install requested but package.json not found in {Directory}.", directory);

        if (skipIfUpToDate)
        {
            string? fingerprint = await ComputeNpmInstallFingerprint(directory, cleanInstall, omitDevDependencies, ignoreScripts, nodeVersion, npmVersion,
                cancellationToken).NoSync();

            if (fingerprint is not null && await IsNpmInstallUpToDate(directory, fingerprint, cancellationToken).NoSync())
            {
                _logger.LogInformation("Skipping npm install in {Directory} (node_modules up-to-date).", directory);
                return string.Empty;
            }
        }

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

        string? installedFingerprint = await ComputeNpmInstallFingerprint(directory, cleanInstall, omitDevDependencies, ignoreScripts, nodeVersion,
            npmVersion, cancellationToken).NoSync();
        await WriteNpmInstallMarkerIfPossible(directory, installedFingerprint, cancellationToken).NoSync();

        return output;
    }

    private async ValueTask<bool> IsNpmInstallUpToDate(string directory, string fingerprint, CancellationToken ct)
    {
        // Must have node_modules
        if (!await _directoryUtil.Exists(GetNodeModulesPath(directory), ct).NoSync())
            return false;

        // Must have marker
        string markerPath = GetMarkerPath(directory);

        if (!await _fileUtil.Exists(markerPath, ct).NoSync())
            return false;

        string stored;

        try
        {
            stored = (await _fileUtil.Read(markerPath, false, cancellationToken: ct).NoSync()).Trim();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }

        if (stored.Length == 0)
            return false;

        return string.Equals(stored, fingerprint, StringComparison.Ordinal);
    }

    private async ValueTask WriteNpmInstallMarker(string directory, string fingerprint, CancellationToken ct)
    {
        string markerPath = GetMarkerPath(directory);

        if (await _fileUtil.Exists(markerPath, ct).NoSync())
        {
            string existing = (await _fileUtil.Read(markerPath, false, cancellationToken: ct).NoSync()).Trim();
            if (string.Equals(existing, fingerprint, StringComparison.Ordinal))
                return;
        }

        await _fileUtil.Write(markerPath, fingerprint, true, ct).NoSync();
    }

    /// <summary>
    /// Writes the npm-install lockhash marker when possible. Swallows errors so marker failures don't fail the build.
    /// </summary>
    private async ValueTask WriteNpmInstallMarkerIfPossible(string directory, string? fingerprint, CancellationToken ct)
    {
        if (fingerprint is null)
            return;

        try
        {
            await WriteNpmInstallMarker(directory, fingerprint, ct).NoSync();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // marker failures shouldn't fail the build
        }
    }

    private async ValueTask<string?> ComputeNpmInstallFingerprint(string directory, bool cleanInstall, bool omitDevDependencies, bool ignoreScripts,
        string? nodeVersion, string npmVersion, CancellationToken ct)
    {
        string packageJson = GetPackageJsonPath(directory);
        if (!await _fileUtil.Exists(packageJson, ct).NoSync())
            return null;

        string packageHash = await ComputeHash(packageJson, ct).NoSync();
        string? lockFile = null;
        string shrinkwrap = GetShrinkwrapPath(directory);

        if (await _fileUtil.Exists(shrinkwrap, ct).NoSync())
            lockFile = shrinkwrap;
        else
        {
            string packageLock = GetPackageLockPath(directory);
            if (await _fileUtil.Exists(packageLock, ct).NoSync())
                lockFile = packageLock;
        }

        if (cleanInstall && lockFile is null)
            return null;

        string lockHash = lockFile is null ? string.Empty : await ComputeHash(lockFile, ct).NoSync();
        string material = $"v2|clean:{cleanInstall}|omitDev:{omitDevDependencies}|ignoreScripts:{ignoreScripts}|node:{nodeVersion}|npm:{npmVersion}|package:{packageHash}|lock:{lockHash}";

        return XxHash3Util.Hash(material);
    }

    /// <summary>
    /// Executes the install pnpm operation.
    /// </summary>
    /// <param name="force">The force.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task containing the result of the operation.</returns>
    public async ValueTask<string> InstallPnpm(bool force = false, CancellationToken cancellationToken = default)
    {
        // Ensure Node/npm exists first
        await EnsureInstalled(null, installIfMissing: true, cancellationToken).NoSync();

        if (!force)
        {
            try
            {
                string existing = await GetPnpmPath(cancellationToken).NoSync();

                if (!existing.IsNullOrWhiteSpace())
                {
                    _logger.LogInformation("pnpm already available at {Path}", existing);
                    return existing;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // pnpm not found, continue to install
            }
        }

        await RunNpmCommand("install -g pnpm", cancellationToken).NoSync();

        // Resolve after install
        string pnpmPath = await GetPnpmPath(cancellationToken).NoSync();

        _logger.LogInformation("pnpm installed at {Path}", pnpmPath);

        return pnpmPath;
    }

    /// <summary>
    /// Executes the run npm command operation.
    /// </summary>
    /// <param name="args">The args.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async ValueTask RunNpmCommand(string args, CancellationToken ct)
    {
        string npm = await GetNpmPath(ct).NoSync();

        await _processUtil.StartAndGetOutput(
            npm,
            args,
            "",
            _npmInstallTimeoutWin,
            ct).NoSync();
    }

    private async ValueTask<string> ComputeHash(string filePath, CancellationToken ct)
    {
        string value = await _fileUtil.Read(filePath, false, ct).NoSync();

        string result = XxHash3Util.Hash(value);

        return result;
    }
}
