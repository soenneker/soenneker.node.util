using System;
using System.Globalization;

namespace Soenneker.Node.Util;

internal static class LinuxNodeInstallCommands
{
    public static string Build(string manager, Version? minimum, string elevate, string setupPath)
    {
        string channel = minimum?.Major.ToString(CultureInfo.InvariantCulture) ?? "current";
        string setup = "'" + setupPath.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";
        string requirement = minimum is null ? "" : ">=" + minimum;
        return manager switch
        {
            "apt-get" => $"""
                {elevate}apt-get -qq update
                {elevate}apt-get -y install ca-certificates curl bash
                curl -fsSL --retry 3 https://deb.nodesource.com/setup_{channel}.x -o {setup}
                {elevate}bash {setup}
                {elevate}apt-get -y install nodejs
                """,
            "dnf" or "yum" => $"""
                {elevate}{manager} install -y ca-certificates bash
                command -v curl >/dev/null 2>&1 || {elevate}{manager} install -y curl
                curl -fsSL --retry 3 https://rpm.nodesource.com/setup_{channel}.x -o {setup}
                {elevate}bash {setup}
                {elevate}{manager} install -y nodejs
                """,
            "zypper" => minimum is null
                ? $"{elevate}zypper --non-interactive refresh\n{elevate}zypper --non-interactive install nodejs npm"
                : $"{elevate}zypper --non-interactive refresh\n{elevate}zypper --non-interactive install nodejs{channel} npm{channel}",
            "pacman" => $"{elevate}pacman -S --needed --noconfirm nodejs npm",
            "apk" => $"""
                if ! {elevate}apk add --no-cache 'nodejs{requirement}' npm; then
                    {elevate}apk add --no-cache 'nodejs-current{requirement}' npm
                fi
                """,
            _ => throw new PlatformNotSupportedException($"Unsupported Linux package manager: {manager}")
        };
    }
}
