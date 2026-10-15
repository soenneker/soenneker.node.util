using System.Threading.Tasks;
using Soenneker.Tests.Attributes.Local;
using Soenneker.Node.Util.Abstract;
using Soenneker.Tests.HostedUnit;

namespace Soenneker.Node.Util.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class NodeUtilTests : HostedUnitTest
{
    private readonly INodeUtil _util;

    public NodeUtilTests(Host host) : base(host)
    {
        _util = Resolve<INodeUtil>(true);
    }

    [Test]
    public void Default()
    {

    }

    [LocalOnly]
    public async ValueTask EnsureInstalled()
    {
        string test = await _util.EnsureInstalled(cancellationToken: System.Threading.CancellationToken.None);
    }

    [LocalOnly]
    public async ValueTask NpmInstall()
    {
        string test = await _util.NpmInstall("C:\\git\\Soenneker\\Quark\\soenneker.quark.gen.tailwind\\test\\Soenneker.Quark.Gen.Tailwind.Demo\\tailwind", cancellationToken: System.Threading.CancellationToken.None);
    }

    [Test]
    public async Task NpmInstall_handles_spaces_and_reuses_cache()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "node util test " + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(directory, "package.json"),
                "{\"name\":\"node-util-smoke\",\"version\":\"1.0.0\",\"private\":true}");
            await _util.NpmInstall(directory, ignoreScripts: true);
            if (!System.IO.File.Exists(System.IO.Path.Combine(directory, "npm-install.lockhash")))
                throw new System.InvalidOperationException("npm install did not write its success marker.");

            // npm does not create node_modules for an empty dependency set.
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(directory, "node_modules"));
            string output = await _util.NpmInstall(directory, ignoreScripts: true);
            if (output != string.Empty)
                throw new System.InvalidOperationException("The unchanged installation was not reused.");
        }
        finally
        {
            System.IO.Directory.Delete(directory, recursive: true);
        }
    }
}

