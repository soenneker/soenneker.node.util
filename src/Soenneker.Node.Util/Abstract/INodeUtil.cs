using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Node.Util.Abstract;

/// <summary>
/// Provides helpers for locating, verifying, and installing Node.js and for running common npm operations.
/// </summary>
public partial interface INodeUtil
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
}