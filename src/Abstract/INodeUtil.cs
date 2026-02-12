using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Node.Util.Abstract;

/// <summary>
/// Provides helpers for locating, verifying, and installing Node.js and for running common npm operations.
/// </summary>
public interface INodeUtil
{
    /// <summary>
    /// Resolves the path to the <c>npx</c> executable for the current OS.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The resolved <c>npx</c> executable path if found; otherwise the default command name (<c>npx</c>).
    /// </returns>
    ValueTask<string> GetNpxPath(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the path to the <c>npm</c> executable for the current OS.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The resolved <c>npm</c> executable path if found; otherwise the default command name (<c>npm</c>).
    /// </returns>
    ValueTask<string> GetNpmPath(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the full path to the Node.js executable by executing a small Node script that prints <c>process.execPath</c>.
    /// </summary>
    /// <param name="nodeCommand">
    /// The node command/executable to run (for example <c>node</c>, <c>node.exe</c>, or an absolute path).
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The resolved Node.js executable path.</returns>
    ValueTask<string> GetNodePath(string nodeCommand = "node", CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures Node.js is installed and (optionally) meets a minimum version.
    /// </summary>
    /// <param name="minVersion">
    /// Optional minimum version string. Examples: <c>20</c>, <c>20.11</c>, <c>v20.11.1</c>.
    /// If <see langword="null"/> or whitespace, any installed version is accepted.
    /// </param>
    /// <param name="installIfMissing">
    /// When <see langword="true"/>, attempts to install Node.js if it cannot be located.
    /// When <see langword="false"/>, the method only probes and will throw if not found.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The resolved Node.js executable path.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="minVersion"/> cannot be parsed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when Node.js cannot be found (or installed when enabled).</exception>
    ValueTask<string> EnsureInstalled(string? minVersion = null, bool installIfMissing = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to locate Node.js and (optionally) verify it meets a minimum version requirement.
    /// </summary>
    /// <param name="minVersion">
    /// Optional minimum version string. Examples: <c>20</c>, <c>20.11</c>, <c>v20.11.1</c>.
    /// If <see langword="null"/> or whitespace, any installed version is accepted.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The resolved Node.js executable path if found and compatible; otherwise <see langword="null"/>.
    /// </returns>
    ValueTask<string?> TryLocate(string? minVersion = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to locate any Node.js installation.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The resolved Node.js executable path if found; otherwise <see langword="null"/>.
    /// </returns>
    ValueTask<string?> TryLocateAny(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to install Node.js.
    /// </summary>
    /// <param name="version">
    /// The target version to install. When <see langword="null"/>, installs the latest available version.
    /// Platform-specific installers may only honor the major version (for example <c>20</c>).
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <remarks>
    /// Installation strategy is OS-specific (for example: apt-get on Linux, winget/choco on Windows, brew on macOS).
    /// This method may require elevated privileges depending on the environment.
    /// </remarks>
    ValueTask TryInstall(Version? version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <c>npm install</c> or <c>npm ci</c> in the specified directory.
    /// </summary>
    /// <param name="directory">The directory containing the npm project.</param>
    /// <param name="cleanInstall">
    /// When <see langword="true"/>, runs <c>npm ci</c>; otherwise runs <c>npm install</c>.
    /// </param>
    /// <param name="omitDevDependencies">When <see langword="true"/>, adds <c>--omit=dev</c>.</param>
    /// <param name="ignoreScripts">When <see langword="true"/>, adds <c>--ignore-scripts</c>.</param>
    /// <param name="noAudit">When <see langword="true"/>, adds <c>--no-audit</c>.</param>
    /// <param name="noFund">When <see langword="true"/>, adds <c>--no-fund</c>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The captured stdout/stderr output from the npm command.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="directory"/> is null/empty/whitespace.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when <paramref name="directory"/> does not exist.</exception>
    ValueTask<string> NpmInstall(
        string directory,
        bool cleanInstall = false,
        bool omitDevDependencies = false,
        bool ignoreScripts = false,
        bool noAudit = true,
        bool noFund = true,
        CancellationToken cancellationToken = default);
}