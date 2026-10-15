using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Node.Util.Abstract;

/// <summary>
/// Provides helpers for locating, verifying, and installing Node.js and for running common npm operations.
/// </summary>
public partial interface INodeUtil
{
    /// <summary>
    /// Ensures Node.js is installed and (optionally) meets a minimum version.
    /// </summary>
    /// <param name="minVersion">
    /// Minimum version string, defaulting to <c>24</c>. Examples: <c>24</c>, <c>24.1</c>, <c>v24.1.0</c>.
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
    ValueTask<string> EnsureInstalled(string? minVersion = "24", bool installIfMissing = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to install Node.js.
    /// </summary>
    /// <param name="version">The target version to install. When <see langword="null"/>, installs the latest available version. Platform-specific installers may only honor the major version (for example <c>20</c>).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the try install operation is complete.</returns>
    /// <remarks>
    /// Installation strategy is OS-specific (NodeSource and apt-get on Debian/Ubuntu, winget/choco on Windows, brew on macOS).
    /// This method may require elevated privileges depending on the environment.
    /// </remarks>
    ValueTask TryInstall(Version? version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <c>npm install</c> or <c>npm ci</c> in the specified directory using the selected Node.js executable.
    /// npm is resolved from that installation or a standard separate npm installation on PATH.
    /// </summary>
    /// <param name="directory">Directory to read from or write to.</param>
    /// <param name="cleanInstall">When <see langword="true"/>, runs <c>npm ci</c>; otherwise runs <c>npm install</c>.</param>
    /// <param name="omitDevDependencies">When <see langword="true"/>, adds <c>--omit=dev</c>.</param>
    /// <param name="ignoreScripts">When <see langword="true"/>, adds <c>--ignore-scripts</c>.</param>
    /// <param name="noAudit">When <see langword="true"/>, adds <c>--no-audit</c>.</param>
    /// <param name="noFund">When <see langword="true"/>, adds <c>--no-fund</c>.</param>
    /// <param name="skipIfUpToDate">Whether skip if up to date.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The captured stdout/stderr output from the npm command.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="directory"/> is empty or invalid.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when <paramref name="directory"/> does not exist.</exception>
    /// <exception cref="FileNotFoundException">Thrown when npm-cli.js cannot be located in a supported installation layout.</exception>
    ValueTask<string> NpmInstall(
        string directory,
        bool cleanInstall = false,
        bool omitDevDependencies = false,
        bool ignoreScripts = false,
        bool noAudit = true,
        bool noFund = true,
        bool skipIfUpToDate = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs pnpm.
    /// </summary>
    /// <param name="force">Whether force.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task whose result is the text returned by install Pnpm.</returns>
    ValueTask<string> InstallPnpm(bool force = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs npm through the selected Node.js executable, resolving bundled or separately installed npm.
    /// The selected Node.js directory is prepended to the child process PATH for lifecycle scripts.
    /// </summary>
    /// <param name="args">Command-line arguments passed to the application.</param>
    /// <param name="ct">Ct for the run npm command operation.</param>
    /// <returns>A task that completes when the run npm command operation is complete.</returns>
    ValueTask RunNpmCommand(string args, CancellationToken ct);
}
