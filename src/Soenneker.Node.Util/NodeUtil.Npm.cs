using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Extensions.ValueTask;

namespace Soenneker.Node.Util;

public sealed partial class NodeUtil
{
    private async ValueTask<NpmCommand> ResolveNpmCommand(string nodePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolver = new NpmCliResolver(_fileUtil);
        string? cli = await resolver.FindBundled(nodePath, cancellationToken).NoSync();
        if (cli is null)
            cli = await resolver.FindSeparate(await GetNpmPath(cancellationToken).NoSync(), cancellationToken).NoSync();
        cancellationToken.ThrowIfCancellationRequested();
        if (cli is null)
            throw new System.IO.FileNotFoundException($"Could not locate npm-cli.js for Node.js '{nodePath}' or npm on PATH. Install npm alongside Node.js or expose a standard npm installation on PATH.");

        _logger.LogInformation("Using Node.js {NodePath} with npm CLI {NpmCli}.", nodePath, cli);
        return new NpmCommand(nodePath, cli, _processUtil);
    }
}
