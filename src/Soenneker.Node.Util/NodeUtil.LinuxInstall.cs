using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Soenneker.Node.Util;

public sealed partial class NodeUtil
{
    private static readonly HttpClient _linuxDownloads = new() { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly SemaphoreSlim _linuxInstallGate = new(1, 1);

    private async ValueTask<string?> ProbeLinuxCache(Version? minimum, CancellationToken cancellationToken)
    {
        string cache = LinuxNodeDistribution.CacheDirectory;
        if (!System.IO.Directory.Exists(cache))
            return null;
        string target = LinuxNodeDistribution.GetTarget(RuntimeInformation.ProcessArchitecture, LinuxNodeDistribution.IsMusl());
        foreach (string directory in System.IO.Directory.EnumerateDirectories(cache, $"node-v*-{target}").OrderDescending(StringComparer.Ordinal))
        {
            string candidate = Path.Combine(directory, "bin", "node");
            string? found = minimum is null ? await ProbeAny(candidate, cancellationToken) : await Probe(candidate, minimum, cancellationToken);
            if (found is not null)
                return found;
        }
        return null;
    }

    private async ValueTask InstallLinux(Version? minimum, CancellationToken cancellationToken)
    {
        await _linuxInstallGate.WaitAsync(cancellationToken);
        try
        {
            if (await ProbeLinuxCache(minimum, cancellationToken) is not null)
                return;

            string target = LinuxNodeDistribution.GetTarget(RuntimeInformation.ProcessArchitecture, LinuxNodeDistribution.IsMusl());
            LinuxNodeRelease? release = null;
            foreach (Uri source in new[] { LinuxNodeDistribution.OfficialSource, LinuxNodeDistribution.CommunitySource })
            {
                string index = await _linuxDownloads.GetStringAsync(new Uri(source, "index.json"), cancellationToken);
                release = LinuxNodeDistribution.SelectRelease(index, minimum, target, source);
                if (release is not null)
                    break;
            }
            if (release is null)
                throw new PlatformNotSupportedException($"No compatible Node.js {minimum?.ToString() ?? "release"} archive is available for {target}.");

            _logger.LogInformation("Installing Node.js {Version} for {Target} from {Source} into the user cache.", release.Version, target, release.Source);
            string cache = LinuxNodeDistribution.CacheDirectory;
            System.IO.Directory.CreateDirectory(cache);
            string staging = Path.Combine(cache, ".install-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(staging);
            try
            {
                Uri releaseUrl = new(release.Source, $"v{release.Version}/");
                string checksums = await _linuxDownloads.GetStringAsync(new Uri(releaseUrl, "SHASUMS256.txt"), cancellationToken);
                string? checksum = checksums.Split('\n').Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    .Where(parts => parts.Length == 2 && parts[1].TrimStart('*') == release.ArchiveName).Select(parts => parts[0]).SingleOrDefault();
                if (checksum is null || checksum.Length != 64 || !checksum.All(Uri.IsHexDigit))
                    throw new InvalidDataException($"No valid SHA-256 checksum is published for {release.ArchiveName}.");

                string archive = Path.Combine(staging, release.ArchiveName);
                await using (Stream download = await _linuxDownloads.GetStreamAsync(new Uri(releaseUrl, release.ArchiveName), cancellationToken))
                await using (FileStream output = File.Create(archive))
                    await download.CopyToAsync(output, cancellationToken);

                await using (FileStream input = File.OpenRead(archive))
                {
                    string actual = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
                    if (!actual.Equals(checksum, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"SHA-256 verification failed for {release.ArchiveName}.");
                    input.Position = 0;
                    await using var gzip = new GZipStream(input, CompressionMode.Decompress);
                    await TarFile.ExtractToDirectoryAsync(gzip, staging, overwriteFiles: false, cancellationToken);
                }

                string extracted = Path.Combine(staging, release.DirectoryName);
                string executable = Path.Combine(extracted, "bin", "node");
                if (await Probe(executable, minimum ?? release.Version, cancellationToken) is null)
                    throw new InvalidOperationException($"Downloaded Node.js {release.Version} cannot run on {target}. Check the host's libc and libstdc++ runtime compatibility; no system packages were changed.");
                string destination = Path.Combine(cache, release.DirectoryName);
                try { System.IO.Directory.Move(extracted, destination); }
                catch (IOException) when (System.IO.Directory.Exists(destination))
                {
                    // Another process may have completed the same installation while this download was running.
                    if (await Probe(Path.Combine(destination, "bin", "node"), minimum ?? release.Version, cancellationToken) is null)
                        throw;
                }
            }
            finally
            {
                System.IO.Directory.Delete(staging, recursive: true);
            }
        }
        finally
        {
            _linuxInstallGate.Release();
        }
    }
}
