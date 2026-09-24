using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace GodotHat.Binding;

/// <summary>
/// Accessors for all view model types, registered by generated module initializers. Editor tooling can enumerate
/// <see cref="Accessors"/> as the view model manifest, after calling <see cref="Discover"/>.
/// </summary>
public static class BindingRegistry
{
    private static readonly ConcurrentDictionary<Type, ViewModelAccessor> accessors = new();
    private static readonly ConcurrentDictionary<Type, ViewModelAccessor?> resolved = new();
    private static readonly ConcurrentDictionary<Type, EnumInfo> enums = new();
    private static readonly ConditionalWeakTable<Assembly, object> scanned = new();
    private static readonly object discoverLock = new();

    /// <summary>All registered accessors.</summary>
    public static ICollection<ViewModelAccessor> Accessors => accessors.Values;

    /// <summary>All registered enum tables.</summary>
    public static ICollection<EnumInfo> Enums => enums.Values;

    /// <summary>Registers an accessor, replacing any for the same type. Called by generated code.</summary>
    public static void Register(ViewModelAccessor accessor)
    {
        accessors[accessor.ViewModelType] = accessor;
        resolved.Clear();
    }

    /// <summary>Registers an enum table, replacing any for the same type. Called by generated code.</summary>
    public static void RegisterEnum(EnumInfo info) => enums[info.EnumType] = info;

    /// <summary>
    /// Finds the accessor for <paramref name="type"/>, or its nearest registered base type, so view models can be bound
    /// through a subclass that has no <c>[ViewModel]</c> of its own.
    /// </summary>
    public static bool TryGet(Type type, [NotNullWhen(true)] out ViewModelAccessor? accessor)
    {
        accessor = resolved.GetOrAdd(type, Resolve);
        return accessor is not null;
    }

    /// <summary>
    /// Finds the accessor for a view model type by its full name, eg from an exported setting, calling
    /// <see cref="Discover"/> if it isn't registered yet.
    /// </summary>
    public static bool TryGetByName(string fullName, [NotNullWhen(true)] out ViewModelAccessor? accessor)
    {
        if (FindByName(fullName, out accessor))
        {
            return true;
        }

        Discover();
        return FindByName(fullName, out accessor);
    }

    /// <summary>
    /// Registers the view models in every assembly that references GodotHat.Binding.Core, loading referenced assemblies
    /// that haven't been yet. Generated registration otherwise waits until code in an assembly runs, so view models
    /// in a library that the game only names in scenes would be missing. Only assemblies not seen before are scanned.
    /// </summary>
    public static void Discover()
    {
        Assembly core = typeof(BindingRegistry).Assembly;
        string coreName = core.GetName().Name!;
        var pending = new Queue<Assembly>(
            (AssemblyLoadContext.GetLoadContext(core) ?? AssemblyLoadContext.Default).Assemblies
            .Concat(AssemblyLoadContext.Default.Assemblies));

        lock (discoverLock)
        {
            while (pending.TryDequeue(out Assembly? assembly))
            {
                if (assembly.IsDynamic || scanned.TryGetValue(assembly, out _))
                {
                    continue;
                }

                scanned.Add(assembly, core);
                AssemblyName[] references = assembly.GetReferencedAssemblies();
                if (!references.Any(r => r.Name == coreName))
                {
                    continue;
                }

                RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
                AssemblyLoadContext context = AssemblyLoadContext.GetLoadContext(assembly) ?? AssemblyLoadContext.Default;
                foreach (AssemblyName reference in references)
                {
                    try
                    {
                        pending.Enqueue(context.LoadFromAssemblyName(reference));
                    }
                    catch (Exception e) when (e is FileNotFoundException or FileLoadException or BadImageFormatException)
                    {
                        // A reference the assembly never loads at runtime, eg a compile-time only dependency
                    }
                }
            }
        }

        resolved.Clear();
    }

    /// <summary>Finds the generated table for enum <paramref name="type"/>.</summary>
    public static bool TryGetEnum(Type type, [NotNullWhen(true)] out EnumInfo? info) => enums.TryGetValue(type, out info);

    /// <summary>Finds the generated table for <typeparamref name="T"/>, if it is an enum used by a view model.</summary>
    public static bool TryGetEnum<T>([NotNullWhen(true)] out IEnumInfo<T>? info)
    {
        info = enums.TryGetValue(typeof(T), out EnumInfo? found) ? found as IEnumInfo<T> : null;
        return info is not null;
    }

    private static bool FindByName(string fullName, [NotNullWhen(true)] out ViewModelAccessor? accessor)
    {
        foreach (ViewModelAccessor candidate in accessors.Values)
        {
            if (candidate.ViewModelType.FullName == fullName)
            {
                accessor = candidate;
                return true;
            }
        }

        accessor = null;
        return false;
    }

    private static ViewModelAccessor? Resolve(Type type)
    {
        for (Type? current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            if (accessors.TryGetValue(current, out ViewModelAccessor? accessor))
            {
                return accessor;
            }

            // A generated accessor registers when its module initializer runs, which only happens once code in that
            // module has run. Seeing a type (eg as a member's declared type) doesn't trigger it.
            RuntimeHelpers.RunModuleConstructor(current.Module.ModuleHandle);
            if (accessors.TryGetValue(current, out accessor))
            {
                return accessor;
            }
        }

        return null;
    }
}
