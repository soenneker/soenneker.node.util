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
        string test = await _util.EnsureInstalled(cancellationToken: CancellationToken);
    }

    [LocalOnly]
    public async ValueTask NpmInstall()
    {
        string test = await _util.NpmInstall("C:\\git\\Soenneker\\Quark\\soenneker.quark.gen.tailwind\\test\\Soenneker.Quark.Gen.Tailwind.Demo\\tailwind", cancellationToken: CancellationToken);
    }
}
