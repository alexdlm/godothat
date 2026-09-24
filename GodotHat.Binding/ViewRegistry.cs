using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Godot;

namespace GodotHat.Binding;

/// <summary>
/// Scenes that present view models, by view model type, for <see cref="ContentPresenterBase"/>s that don't list the
/// type in their own <see cref="ContentPresenterBase.Views"/>.
/// </summary>
/// <example>
/// <code>
/// ViewRegistry.Register&lt;SettingsPageViewModel&gt;("res://ui/settings_page.tscn");
/// </code>
/// </example>
public static class ViewRegistry
{
    private static readonly ConcurrentDictionary<Type, Lazy<PackedScene>> scenes = new();

    /// <summary>Registers the scene at <paramref name="scenePath"/>, loaded on first use, for <typeparamref name="TViewModel"/>.</summary>
    public static void Register<TViewModel>(string scenePath) => Register(typeof(TViewModel), scenePath);

    /// <summary>Registers the scene at <paramref name="scenePath"/>, loaded on first use, for <paramref name="viewModelType"/>.</summary>
    public static void Register(Type viewModelType, string scenePath) =>
        scenes[viewModelType] = new Lazy<PackedScene>(() => GD.Load<PackedScene>(scenePath));

    /// <summary>Registers <paramref name="scene"/> for <paramref name="viewModelType"/>.</summary>
    public static void Register(Type viewModelType, PackedScene scene) =>
        scenes[viewModelType] = new Lazy<PackedScene>(scene);

    /// <summary>Finds the scene for <paramref name="viewModelType"/>, or for its nearest registered base type.</summary>
    public static bool TryGetScene(Type viewModelType, [NotNullWhen(true)] out PackedScene? scene)
    {
        for (Type? type = viewModelType; type is not null; type = type.BaseType)
        {
            if (scenes.TryGetValue(type, out Lazy<PackedScene>? found))
            {
                scene = found.Value;
                return true;
            }
        }

        scene = null;
        return false;
    }
}
