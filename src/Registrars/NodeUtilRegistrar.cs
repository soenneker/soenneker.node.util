using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Soenneker.Node.Util.Abstract;
using Soenneker.Utils.Directory.Registrars;
using Soenneker.Utils.Process.Registrars;

namespace Soenneker.Node.Util.Registrars;

/// <summary>
/// A utility library for Node related operations
/// </summary>
public static class NodeUtilRegistrar
{
    /// <summary>
    /// Adds <see cref="INodeUtil"/> as a singleton service. <para/>
    /// </summary>
    public static IServiceCollection AddNodeUtilAsSingleton(this IServiceCollection services)
    {
        services.AddDirectoryUtilAsSingleton().AddProcessUtilAsSingleton().TryAddSingleton<INodeUtil, NodeUtil>();

        return services;
    }

    /// <summary>
    /// Adds <see cref="INodeUtil"/> as a scoped service. <para/>
    /// </summary>
    public static IServiceCollection AddNodeUtilAsScoped(this IServiceCollection services)
    {
        services.AddDirectoryUtilAsScoped().AddProcessUtilAsScoped().TryAddScoped<INodeUtil, NodeUtil>();

        return services;
    }
}
