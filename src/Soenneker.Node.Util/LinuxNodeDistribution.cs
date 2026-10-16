using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Soenneker.Node.Util;

internal static class LinuxNodeDistribution
{
    public static readonly Uri OfficialSource = new("https://nodejs.org/dist/");
    public static readonly Uri CommunitySource = new("https://unofficial-builds.nodejs.org/download/release/");

    public static string CacheDirectory => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } xdg && Path.IsPathRooted(xdg)
            ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"),
        "Soenneker", "Node");

    public static bool IsMusl() => RuntimeInformation.RuntimeIdentifier.Contains("musl", StringComparison.Ordinal) ||
        new[] { "/lib", "/usr/lib", "/lib64" }.Any(path => Directory.Exists(path) && Directory.EnumerateFiles(path, "ld-musl-*.so.1").Any());

    public static string GetTarget(Architecture architecture, bool musl)
    {
        string arch = architecture.ToString() switch
        {
            "X64" => "x64",
            "Arm64" => "arm64",
            "Arm" => "armv7l",
            "S390x" => "s390x",
            "Ppc64le" => "ppc64le",
            "LoongArch64" => "loong64",
            "RiscV64" => "riscv64",
            _ => throw new PlatformNotSupportedException($"Node.js binary installation is not supported for Linux {architecture}.")
        };
        return $"linux-{arch}" + (musl ? "-musl" : "");
    }

    public static LinuxNodeRelease? SelectRelease(string index, Version? minimum, string target, Uri source)
    {
        using JsonDocument document = JsonDocument.Parse(index);
        LinuxNodeRelease? best = null;
        foreach (JsonElement entry in document.RootElement.EnumerateArray())
        {
            string? versionText = entry.GetProperty("version").GetString();
            if (versionText is null || !Version.TryParse(versionText.TrimStart('v'), out Version? version) ||
                (minimum is not null && (version.Major != minimum.Major || version < minimum)) ||
                !entry.GetProperty("files").EnumerateArray().Any(file => file.GetString() == target))
                continue;
            if (best is null || version > best.Version)
                best = new LinuxNodeRelease(version, target, source);
        }
        return best;
    }
}
