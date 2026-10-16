using System;

namespace Soenneker.Node.Util;

internal sealed record LinuxNodeRelease(Version Version, string Target, Uri Source)
{
    public string DirectoryName => $"node-v{Version}-{Target}";
    public string ArchiveName => DirectoryName + ".tar.gz";
}
