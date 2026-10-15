using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Node.Util;

/// <summary>Runs an npm CLI script with a selected Node.js executable.</summary>
/// <param name="NodePath">Path to the Node.js executable.</param>
/// <param name="CliPath">Path to npm-cli.js.</param>
public sealed record NpmCommand(string NodePath, string CliPath)
{
    /// <summary>Runs npm, returning stdout and including captured output in failure diagnostics.</summary>
    /// <param name="arguments">Arguments passed to the npm CLI.</param>
    /// <param name="directory">Working directory, or empty to use the current directory.</param>
    /// <param name="timeout">Maximum duration before bounded process cleanup begins.</param>
    /// <param name="cancellationToken">Token used to cancel execution.</param>
    public async Task<string> Run(string arguments, string directory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo
        {
            FileName = NodePath,
            Arguments = $"\"{CliPath.Replace("\"", "\\\"")}\" {arguments}",
            WorkingDirectory = string.IsNullOrWhiteSpace(directory) ? Environment.CurrentDirectory : directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // npm lifecycle scripts must find the same Node as npm itself.
        startInfo.Environment["PATH"] = Path.GetDirectoryName(NodePath) + Path.PathSeparator + startInfo.Environment["PATH"];
        using var process = new System.Diagnostics.Process { StartInfo = startInfo };
        using var readCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!process.Start())
            throw new InvalidOperationException($"Could not start Node.js '{NodePath}' for npm '{CliPath}'.");
        process.StandardInput.Close();
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        Task completion = Task.WhenAll(process.WaitForExitAsync(cancellationToken),
            Capture(process.StandardOutput, stdout, readCancellation.Token),
            Capture(process.StandardError, stderr, readCancellation.Token));
        try
        {
            await completion.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            bool exited = process.HasExited;
            try
            {
                if (!exited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }

            // Bound cleanup as well: descendants can keep redirected pipes open.
            try { await completion.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (Exception) { }
            await readCancellation.CancelAsync().ConfigureAwait(false);
            _ = completion.ContinueWith(static task => _ = task.Exception,
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            if (ex is OperationCanceledException)
                throw;
            throw new TimeoutException($"npm exceeded {timeout.TotalSeconds} seconds. Node: '{NodePath}'; CLI: '{CliPath}'; directory: '{directory}'; process exited before cleanup: {exited}." +
                $"{Environment.NewLine}stdout: {Tail(stdout)}{Environment.NewLine}stderr: {Tail(stderr)}", ex);
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"npm exited with code {process.ExitCode}. Node: '{NodePath}'; CLI: '{CliPath}'.{Environment.NewLine}{Tail(stderr)}{Environment.NewLine}{Tail(stdout)}");
        return stdout.ToString();
    }

    private static async Task Capture(StreamReader reader, StringBuilder output, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
        {
            lock (output)
                output.Append(buffer, 0, count);
        }
    }

    private static string Tail(StringBuilder output)
    {
        lock (output)
        {
            int start = Math.Max(0, output.Length - 4000);
            return output.ToString(start, output.Length - start);
        }
    }
}
