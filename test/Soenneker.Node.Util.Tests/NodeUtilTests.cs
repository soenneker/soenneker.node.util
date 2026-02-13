using System.Threading.Tasks;
using Soenneker.Facts.Local;
using Soenneker.Node.Util.Abstract;
using Soenneker.Tests.FixturedUnit;
using Xunit;

namespace Soenneker.Node.Util.Tests;

[Collection("Collection")]
public sealed class NodeUtilTests : FixturedUnitTest
{
    private readonly INodeUtil _util;

    public NodeUtilTests(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
    {
        _util = Resolve<INodeUtil>(true);
    }

    [Fact]
    public void Default()
    {

    }

    [LocalFact]
    public async ValueTask EnsureInstalled()
    {
        string test = await _util.EnsureInstalled(cancellationToken: CancellationToken);
    }

    [LocalFact]
    public async ValueTask NpmInstall()
    {
        string test = await _util.NpmInstall("C:\\git\\Soenneker\\Quark\\soenneker.quark.gen.tailwind\\test\\Soenneker.Quark.Gen.Tailwind.Demo\\tailwind", cancellationToken: CancellationToken);
    }
}
