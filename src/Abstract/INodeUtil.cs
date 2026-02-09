using System;
using System.Diagnostics.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Node.Util.Abstract;

/// <summary>
/// A utility library for Node related operations (cross-platform).
/// </summary>
public interface INodeUtil
{
    /// <summary>
    /// Returns the absolute path to the Node.js executable resolved from <paramref name="nodeCommand"/>.
    /// </summary>
    /// <param name="nodeCommand">
    /// Command or launcher to invoke (e.g., <c>"node"</c>). Defaults to <c>"node"</c>.
    /// </param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    [Pure]
    ValueTask<string> GetNodePath(string nodeCommand = "node", CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures that Node.js is installed. When <paramref name="minVersion"/> is null or empty, accepts any version and installs the latest if missing.
    /// </summary>
    /// <param name="minVersion">Minimum acceptable version (e.g., <c>"20"</c> or <c>"20.10"</c>), or null/empty for any version (install latest if missing).</param>
    /// <param name="installIfMissing">Attempt to install via winget / brew / apt if not found.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>Full path to the node executable that satisfies the requirement.</returns>
    ValueTask<string> EnsureInstalled(string? minVersion = null, bool installIfMissing = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to locate an installed Node.js that satisfies the given version (major, or major.minor).
    /// When <paramref name="minVersion"/> is null or empty, returns any installed node (same as <see cref="TryLocateAny"/>).
    /// Does not install; returns null if not found.
    /// </summary>
    /// <param name="minVersion">Minimum version (e.g., <c>"20"</c> or <c>"20.10"</c>), or null/empty for any version.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>Full path to node executable, or null if not found.</returns>
    [Pure]
    ValueTask<string?> TryLocate(string? minVersion = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to locate any installed Node.js (any version). Does not install; returns null if none found.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>Full path to node executable, or null if not found.</returns>
    [Pure]
    ValueTask<string?> TryLocateAny(CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes the platform-appropriate package manager to install Node.js.
    /// When <paramref name="version"/> is null, installs the latest version; otherwise installs the specified major (and optionally minor).
    /// </summary>
    /// <param name="version">Version to install (e.g., 20 or 20.10), or null to install the latest.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    ValueTask TryInstall(Version? version, CancellationToken cancellationToken = default);
}
