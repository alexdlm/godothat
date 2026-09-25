namespace GodotHat.Binding;

/// <summary>Creates view models for binding roots that aren't given one, eg a scene run on its own.</summary>
public interface IViewModelActivator
{
    /// <summary>Creates a view model of <paramref name="viewModelType"/>. The caller owns and disposes it.</summary>
    object Create(Type viewModelType);
}

/// <summary>Creates view models with their public parameterless constructor, through the generated accessor.</summary>
public sealed class DefaultViewModelActivator : IViewModelActivator
{
    /// <summary>The shared instance.</summary>
    public static DefaultViewModelActivator Instance { get; } = new();

    /// <inheritdoc />
    public object Create(Type viewModelType)
    {
        if (!BindingRegistry.TryGet(viewModelType, out ViewModelAccessor? accessor) || accessor.ViewModelType != viewModelType)
        {
            throw new InvalidOperationException($"{viewModelType.FullName} is not a [ViewModel].");
        }

        return accessor.CreateInstance();
    }
}

/// <summary>
/// Creates view models with their constructor's parameters from a service provider, such as a Jab
/// <c>[ServiceProvider]</c>, through the generated accessor. View models aren't registered with the provider, so it
/// never tracks or disposes them; whoever creates a view model disposes it.
/// </summary>
/// <example>
/// <code>
/// [ServiceProvider]
/// [Singleton(typeof(IBuildQueue), typeof(BuildQueue))]
/// public partial class GameServices;
///
/// GodotBinding.Activator = new ServiceProviderViewModelActivator(new GameServices());
/// </code>
/// </example>
public sealed class ServiceProviderViewModelActivator(IServiceProvider services) : IViewModelActivator
{
    /// <inheritdoc />
    public object Create(Type viewModelType)
    {
        if (!BindingRegistry.TryGet(viewModelType, out ViewModelAccessor? accessor) || accessor.ViewModelType != viewModelType)
        {
            throw new InvalidOperationException($"{viewModelType.FullName} is not a [ViewModel].");
        }

        return accessor.CanCreateInstanceWithServices ? accessor.CreateInstance(services) : accessor.CreateInstance();
    }
}

/// <summary>Resolves view model constructor parameters for generated accessors.</summary>
public static class ServiceResolver
{
    /// <summary>The service of type <typeparamref name="T"/>, which must be available.</summary>
    public static T Required<T>(IServiceProvider services, Type viewModelType) =>
        services.GetService(typeof(T)) is T service
            ? service
            : throw new InvalidOperationException($"{viewModelType.Name} needs a {typeof(T).Name}, which the service provider doesn't provide.");

    /// <summary>The service of type <typeparamref name="T"/>, or default if it isn't available.</summary>
    public static T? Optional<T>(IServiceProvider services) => services.GetService(typeof(T)) is T service ? service : default;
}
