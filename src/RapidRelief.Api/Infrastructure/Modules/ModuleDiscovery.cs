using System.Reflection;

namespace RapidRelief.Api.Infrastructure.Modules;

public static class ModuleDiscovery
{
    public static IReadOnlyList<IFeatureModule> Discover(Assembly assembly)
    {
        return assembly.GetTypes()
            .Where(type => typeof(IFeatureModule).IsAssignableFrom(type)
                           && type is { IsAbstract: false, IsInterface: false }
                           && type.GetConstructor(Type.EmptyTypes) is not null)
            .Select(type => (IFeatureModule)Activator.CreateInstance(type)!)
            .OrderBy(module => module.Order)
            .ThenBy(module => module.Name, StringComparer.Ordinal)
            .ToList();
    }
}
