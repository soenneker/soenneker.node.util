namespace Soenneker.Node.Util;

internal sealed record NpmInstallRequest(
    NodeUtil Owner,
    string RequestKey,
    string Npm,
    string? NodeVersion,
    string NpmVersion,
    string? Fingerprint,
    bool CleanInstall,
    bool OmitDevDependencies,
    bool IgnoreScripts,
    bool NoAudit,
    bool NoFund,
    bool SkipIfUpToDate);
