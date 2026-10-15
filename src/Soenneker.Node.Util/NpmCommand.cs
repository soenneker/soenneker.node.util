using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Extensions.ValueTask;
using Soenneker.Utils.Process.Abstract;

namespace Soenneker.Node.Util;

/// <summary>Runs an npm CLI script with a selected Node.js executable.</summary>
/// <param name="NodePath">Path to the Node.js executable.</param>
/// <param name="CliPath">Path to npm-cli.js.</param>
/// <param name="ProcessUtil">Utility responsible for process execution and lifecycle management.</param>
public sealed record NpmCommand(string NodePath, string CliPath, IProcessUtil ProcessUtil)
{
    /// <summary>Runs npm and returns the captured output lines, including standard error.</summary>
    /// <param name="arguments">Arguments passed to the npm CLI.</param>
    /// <param name="directory">Working directory, or empty to use the current directory.</param>
    /// <param name="timeout">Maximum execution duration passed to the process utility.</param>
    /// <param name="cancellationToken">Token used to cancel execution.</param>
    public async Task<string> Run(string arguments, string directory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = Path.GetDirectoryName(NodePath) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
        };

        try
        {
            List<string> output = await ProcessUtil.Start(NodePath, workingDirectory: directory,
                arguments: $"\"{CliPath.Replace("\"", "\\\"")}\" {arguments}", timeout: timeout,
                environmentalVars: environment, cancellationToken: cancellationToken).NoSync();
            return string.Join(Environment.NewLine, output);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"npm exceeded {timeout.TotalSeconds} seconds. Node: '{NodePath}'; CLI: '{CliPath}'; directory: '{directory}'.", ex);
        }
    }
}
