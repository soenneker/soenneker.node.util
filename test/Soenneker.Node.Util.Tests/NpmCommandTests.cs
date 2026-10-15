using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Node.Util.Abstract;
using Soenneker.Utils.Process.Abstract;
using Soenneker.Tests.HostedUnit;

namespace Soenneker.Node.Util.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class NpmCommandTests(Host host) : HostedUnitTest(host)
{
    [Test]
    public async Task Selected_node_is_used_for_child_commands()
    {
        string node = await Resolve<INodeUtil>(true).EnsureInstalled(installIfMissing: false);
        await WithScript("console.log(require('child_process').execFileSync('node', ['-p', 'process.execPath'], {encoding:'utf8'}).trim());", async cli =>
        {
            string output = await new NpmCommand(node, cli, Resolve<IProcessUtil>(true)).Run("", "", TimeSpan.FromSeconds(30), CancellationToken.None);
            string expected = File.ResolveLinkTarget(node, true)?.FullName ?? node;
            if (!string.Equals(output.Trim(), expected, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidOperationException("The child command did not use the selected Node: " + output);
        });
    }

    [Test]
    public async Task Drains_both_output_streams_without_deadlock()
    {
        string node = await Resolve<INodeUtil>(true).EnsureInstalled(installIfMissing: false);
        await WithScript("process.stdout.write('x'.repeat(200000)); process.stderr.write('y'.repeat(200000));", async cli =>
        {
            string output = await new NpmCommand(node, cli, Resolve<IProcessUtil>(true)).Run("", "", TimeSpan.FromSeconds(30), CancellationToken.None);
            if (!output.Contains(new string('x', 200000)) || !output.Contains(new string('y', 200000)))
                throw new InvalidOperationException("Output was truncated or not drained.");
        });
    }

    [Test]
    public async Task Nonzero_exit_preserves_diagnostics()
    {
        string node = await Resolve<INodeUtil>(true).EnsureInstalled(installIfMissing: false);
        await WithScript("console.error('npm failure details'); process.exitCode = 7;", async cli =>
        {
            try { await new NpmCommand(node, cli, Resolve<IProcessUtil>(true)).Run("", "", TimeSpan.FromSeconds(30), CancellationToken.None); }
            catch (InvalidOperationException ex) when (ex.ToString().Contains("code 7") && ex.ToString().Contains("npm failure details")) { return; }
            throw new InvalidOperationException("Expected npm exit code and stderr.");
        });
    }

    [Test]
    public async Task Timeout_reports_command_context()
    {
        string node = await Resolve<INodeUtil>(true).EnsureInstalled(installIfMissing: false);
        await WithScript("console.error('started'); setInterval(() => {}, 1000);", async cli =>
        {
            try { await new NpmCommand(node, cli, Resolve<IProcessUtil>(true)).Run("", "", TimeSpan.FromSeconds(2), CancellationToken.None); }
            catch (TimeoutException ex) when (ex.Message.Contains(cli) && ex.Message.Contains(node)) { return; }
            throw new InvalidOperationException("Expected timeout diagnostics.");
        });
    }

    [Test]
    public async Task Cancellation_is_not_converted_to_timeout()
    {
        string node = await Resolve<INodeUtil>(true).EnsureInstalled(installIfMissing: false);
        await WithScript("setInterval(() => {}, 1000);", async cli =>
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            try { await new NpmCommand(node, cli, Resolve<IProcessUtil>(true)).Run("", "", TimeSpan.FromSeconds(30), cancellation.Token); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
            throw new InvalidOperationException("Expected cancellation.");
        });
    }

    private static async Task WithScript(string source, Func<string, Task> action)
    {
        string root = Path.Combine(Path.GetTempPath(), "npm command " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string cli = Path.Combine(root, "npm-cli.js");
            await File.WriteAllTextAsync(cli, source);
            await action(cli);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
