using System.Diagnostics.CodeAnalysis;

namespace GodotHat.Binding;

/// <summary>
/// The bindable members of a view model type. Generated for each <c>[ViewModel]</c> class and registered with
/// <see cref="BindingRegistry"/>; also serves as the member manifest for editor tooling.
/// </summary>
public sealed class ViewModelAccessor
{
    private readonly Dictionary<string, MemberAccessor> membersByName;
    private readonly Func<object>? factory;
    private readonly Func<IServiceProvider, object>? serviceFactory;

    /// <summary>Creates an accessor.</summary>
    /// <param name="viewModelType">The view model type.</param>
    /// <param name="members">Its bindable members.</param>
    /// <param name="factory">Creates it with its parameterless constructor, if it has one.</param>
    /// <param name="serviceFactory">Creates it with its constructor's parameters from a service provider.</param>
    public ViewModelAccessor(
        Type viewModelType,
        IReadOnlyList<MemberAccessor> members,
        Func<object>? factory = null,
        Func<IServiceProvider, object>? serviceFactory = null)
    {
        this.ViewModelType = viewModelType;
        this.Members = members;
        this.factory = factory;
        this.serviceFactory = serviceFactory;
        this.membersByName = new Dictionary<string, MemberAccessor>(members.Count, StringComparer.Ordinal);
        foreach (MemberAccessor member in members)
        {
            this.membersByName[member.Name] = member;
        }
    }

    /// <summary>The view model type.</summary>
    public Type ViewModelType { get; }

    /// <summary>All bindable members, in declaration order.</summary>
    public IReadOnlyList<MemberAccessor> Members { get; }

    /// <summary>Whether <see cref="CreateInstance()"/> can construct the view model.</summary>
    public bool CanCreateInstance => this.factory is not null;

    /// <summary>Finds a member by name.</summary>
    public bool TryGetMember(string name, [NotNullWhen(true)] out MemberAccessor? member) =>
        this.membersByName.TryGetValue(name, out member);

    /// <summary>Whether <see cref="CreateInstance(IServiceProvider)"/> can construct the view model.</summary>
    public bool CanCreateInstanceWithServices => this.serviceFactory is not null;

    /// <summary>Constructs the view model with its parameterless constructor.</summary>
    public object CreateInstance() =>
        this.factory?.Invoke() ??
        throw new InvalidOperationException($"{this.ViewModelType} has no public parameterless constructor.");

    /// <summary>
    /// Constructs the view model with its constructor's parameters resolved from <paramref name="services"/>. The
    /// provider only supplies dependencies; the view model itself is never registered with it, so the provider can't
    /// dispose it.
    /// </summary>
    public object CreateInstance(IServiceProvider services) =>
        this.serviceFactory?.Invoke(services) ??
        throw new InvalidOperationException($"{this.ViewModelType} has no public constructor to create it with services.");

    /// <inheritdoc />
    public override string ToString() => $"{this.ViewModelType.FullName} ({this.Members.Count} members)";
}
