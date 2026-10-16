using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Soenneker.Extensions.ValueTask;
using Soenneker.Extensions.Task;

namespace Soenneker.Node.Util;

public sealed partial class NodeUtil
{
    private static readonly SemaphoreSlim _linuxInstallGate = new(1, 1);
    private static readonly string[] _linuxPackageManagers = ["apt-get", "dnf", "yum", "zypper", "pacman", "apk"];

    private async ValueTask InstallLinux(Version? minimum, CancellationToken cancellationToken)
    {
        await _linuxInstallGate.WaitAsync(cancellationToken).NoSync();
        try
        {
            if (await TryLocate(minimum?.ToString(), cancellationToken).NoSync() is not null)
                return;

            string? manager = null;
            foreach (string candidate in _linuxPackageManagers)
            {
                if (await _processUtil.CommandExistsAndRuns(candidate, "--version", _existsTimeout, cancellationToken).NoSync())
                {
                    manager = candidate;
                    break;
                }
            }
            if (manager is null)
                throw new PlatformNotSupportedException("No supported Linux package manager found. Supported managers: apt-get, dnf, yum, zypper, pacman, apk.");

            string uid = (await _processUtil.StartAndGetOutput("id", "-u", "", _probeTimeout, cancellationToken).NoSync()).Trim();
            string elevate = "";
            if (uid != "0")
            {
                if (!await _processUtil.CommandExistsAndRuns("sudo", "-n true", _existsTimeout, cancellationToken).NoSync())
                    throw new UnauthorizedAccessException($"Installing Node.js with {manager} requires root or passwordless sudo.");
                elevate = "sudo -n ";
            }

            string temp = await _directoryUtil.CreateTempDirectory(cancellationToken).NoSync();
            try
            {
                string script = Path.Combine(temp, "install-node.sh");
                string setup = Path.Combine(temp, "nodesource-setup.sh");
                string commands = LinuxNodeInstallCommands.Build(manager, minimum, elevate, setup);
                await _fileUtil.Write(script, "set -eu\n" + commands.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n", cancellationToken: cancellationToken).NoSync();
                _logger.LogInformation("Installing Node.js {Version} with {PackageManager}.", minimum?.ToString() ?? "latest", manager);
                await _processUtil.StartAndGetOutput("/bin/sh", $"\"{script}\"", temp, TimeSpan.FromMinutes(15), cancellationToken).NoSync();
                if (await TryLocate(minimum?.ToString(), cancellationToken).NoSync() is null)
                    throw new InvalidOperationException($"{manager} did not provide runnable Node.js {minimum?.ToString() ?? "latest"}. Check repository availability, architecture support, and older Node installations shadowing the system executable on PATH.");
            }
            finally
            {
                await _directoryUtil.Delete(temp, CancellationToken.None).NoSync();
            }
        }
        finally
        {
            _linuxInstallGate.Release();
        }
    }
}
