using System.Diagnostics.CodeAnalysis;
using R3;

namespace GodotHat.Binding;

/// <summary>
/// Creates bindings from <see cref="BindingSpec"/>s between data contexts and targets. Godot-free: the Godot adapter
/// supplies targets, and a dispatcher that delivers updates on Godot's main thread.
/// </summary>
public sealed partial class BindingEngine
{
    /// <summary>Creates an engine.</summary>
    /// <param name="dispatcher">Delivers updates on the main thread.</param>
    /// <param name="errors">Receives binding errors.</param>
    /// <param name="converters">Converters by id; <see cref="BindingConverterRegistry.Default"/> if null.</param>
    public BindingEngine(
        BindingDispatcher dispatcher,
        IBindingErrorSink errors,
        BindingConverterRegistry? converters = null)
    {
        this.Dispatcher = dispatcher;
        this.Errors = errors;
        this.Converters = converters ?? BindingConverterRegistry.Default;
    }

    /// <summary>Delivers updates on the main thread.</summary>
    public BindingDispatcher Dispatcher { get; }

    /// <summary>Receives binding errors.</summary>
    public IBindingErrorSink Errors { get; }

    /// <summary>Converters by id.</summary>
    public BindingConverterRegistry Converters { get; }

    /// <summary>
    /// Binds a target property. Errors are reported and leave the target showing its fallback.
    /// </summary>
    /// <returns>Disposing it ends the binding.</returns>
    public IDisposable BindProperty(DataContext context, BindingSpec spec, IBindingTarget target)
    {
        if (!this.TryResolve(context, spec, target.Description, out Observable<object?>? owners, out MemberAccessor? leaf))
        {
            target.SetFallback();
            return Disposable.Empty;
        }

        return leaf.Accept(new PropertyBinder(this, spec, target, owners));
    }

    /// <summary>Binds a target signal to a command.</summary>
    /// <returns>Disposing it ends the binding.</returns>
    public IDisposable BindCommand(DataContext context, BindingSpec spec, ICommandTarget target)
    {
        if (!this.TryResolve(context, spec, target.Description, out Observable<object?>? owners, out MemberAccessor? leaf))
        {
            target.SetAvailable(false);
            return Disposable.Empty;
        }

        return leaf.Accept(new CommandBinder(this, spec, target, owners, context));
    }

    /// <summary>Binds a view model observable to a target method.</summary>
    /// <returns>Disposing it ends the binding.</returns>
    public IDisposable BindEvent(DataContext context, BindingSpec spec, IEventTarget target)
    {
        if (!this.TryResolve(context, spec, target.Description, out Observable<object?>? owners, out MemberAccessor? leaf))
        {
            return Disposable.Empty;
        }

        return leaf.Accept(new EventBinder(this, spec, target, owners));
    }

    /// <summary>Binds a container's children to a collection.</summary>
    /// <returns>Disposing it ends the binding.</returns>
    public IDisposable BindItems(DataContext context, BindingSpec spec, IItemsTarget target)
    {
        if (!this.TryResolve(context, spec, target.Description, out Observable<object?>? owners, out MemberAccessor? leaf))
        {
            target.Reset(ReadOnlySpan<object>.Empty);
            return Disposable.Empty;
        }

        return leaf.Accept(new ItemsBinder(this, spec, target, owners));
    }

    /// <summary>
    /// Creates the data context for a subtree from a member of <paramref name="context"/>. The new context's values
    /// update on the main thread, and it is null while the path can't be resolved.
    /// </summary>
    /// <param name="context">The context the path starts from.</param>
    /// <param name="spec">The context binding.</param>
    /// <param name="targetDescription">Describes the target for errors.</param>
    /// <param name="subscription">Disposing it ends the binding.</param>
    public DataContext BindContext(
        DataContext context,
        BindingSpec spec,
        string targetDescription,
        out IDisposable subscription)
    {
        if (!this.TryResolve(context, spec, targetDescription, out Observable<object?>? owners, out MemberAccessor? leaf))
        {
            subscription = Disposable.Empty;
            return new DataContext(typeof(object), Observable.Return<object?>(null), context);
        }

        if (leaf is not ValueMember member || leaf.ValueType.IsValueType)
        {
            this.Report(targetDescription, spec, $"{leaf.Name} can't be a data context; it must be a view model or other object.");
            subscription = Disposable.Empty;
            return new DataContext(typeof(object), Observable.Return<object?>(null), context);
        }

        var values = new ReactiveProperty<object?>(null, ReferenceEqualityComparer.Instance);
        IDisposable source = member.SwitchOwners(owners)
            .ObserveOnMainThread(this.Dispatcher, coalesce: false)
            .Subscribe(values, static (value, target) => target.Value = value);
        subscription = Disposable.Combine(source, values);
        return new DataContext(member.ValueType, values, context);
    }

    internal void Report(string target, BindingSpec spec, string message, Exception? exception = null) =>
        this.Errors.Report(new BindingError(target, spec.Path, message, exception));

    private bool TryResolve(
        DataContext context,
        BindingSpec spec,
        string target,
        [NotNullWhen(true)] out Observable<object?>? owners,
        [NotNullWhen(true)] out MemberAccessor? leaf)
    {
        owners = null;
        leaf = null;

        DataContext? source = spec.Source == BindingSourceKind.ParentContext ? context.Parent : context;
        if (source is null)
        {
            this.Report(target, spec, "There is no parent data context.");
            return false;
        }

        if (!BindingPath.TryCompile(source.Type, spec.Path, out BindingPath? path, out string? error))
        {
            this.Report(target, spec, error);
            return false;
        }

        owners = path.ObserveOwners(source.Values);
        leaf = path.Leaf ?? source.Self;
        return true;
    }

    private sealed class PropertyBinder(
        BindingEngine engine,
        BindingSpec spec,
        IBindingTarget target,
        Observable<object?> owners) : IMemberVisitor<IDisposable>
    {
        public IDisposable VisitValue<T>(ValueMember<T> member)
        {
            var binding = new PropertyBinding<T>(engine, spec, target, member);
            binding.Start(owners);
            return binding;
        }

        public IDisposable VisitCommand<TParam>(CommandMember<TParam> member)
        {
            engine.Report(target.Description, spec, $"{member.Name} is a command; bind it to a signal instead.");
            target.SetFallback();
            return Disposable.Empty;
        }

        // A collection bound to a property binds its item count.
        public IDisposable VisitItems<TItem>(ItemsMember<TItem> member) => this.VisitValue(new ItemsCountMember<TItem>(member));
    }

    private sealed class CommandBinder(
        BindingEngine engine,
        BindingSpec spec,
        ICommandTarget target,
        Observable<object?> owners,
        DataContext context) : IMemberVisitor<IDisposable>
    {
        public IDisposable VisitValue<T>(ValueMember<T> member)
        {
            engine.Report(target.Description, spec, $"{member.Name} is not a command.");
            target.SetAvailable(false);
            return Disposable.Empty;
        }

        public IDisposable VisitCommand<TParam>(CommandMember<TParam> member)
        {
            var binding = new CommandBinding<TParam>(engine, spec, target, member);
            binding.Start(owners, context);
            return binding;
        }

        public IDisposable VisitItems<TItem>(ItemsMember<TItem> member)
        {
            engine.Report(target.Description, spec, $"{member.Name} is a collection, not a command.");
            target.SetAvailable(false);
            return Disposable.Empty;
        }
    }

    private sealed class EventBinder(
        BindingEngine engine,
        BindingSpec spec,
        IEventTarget target,
        Observable<object?> owners) : IMemberVisitor<IDisposable>
    {
        public IDisposable VisitValue<T>(ValueMember<T> member)
        {
            var binding = new EventBinding<T>(engine, spec, target);
            binding.Start(new MemberSwitch<T>(owners, member, null));
            return binding;
        }

        public IDisposable VisitCommand<TParam>(CommandMember<TParam> member)
        {
            engine.Report(target.Description, spec, $"{member.Name} is a command, not an observable.");
            return Disposable.Empty;
        }

        public IDisposable VisitItems<TItem>(ItemsMember<TItem> member)
        {
            engine.Report(target.Description, spec, $"{member.Name} is a collection, not an observable.");
            return Disposable.Empty;
        }
    }

    private sealed class ItemsBinder(
        BindingEngine engine,
        BindingSpec spec,
        IItemsTarget target,
        Observable<object?> owners) : IMemberVisitor<IDisposable>
    {
        public IDisposable VisitValue<T>(ValueMember<T> member) => this.NotItems(member);

        public IDisposable VisitCommand<TParam>(CommandMember<TParam> member) => this.NotItems(member);

        public IDisposable VisitItems<TItem>(ItemsMember<TItem> member)
        {
            if (!target.CanAccept<TItem>(out string? error))
            {
                engine.Report(target.Description, spec, error);
                return Disposable.Empty;
            }

            var binding = new ItemsBinding<TItem>(engine, spec, target, member);
            binding.Start(owners);
            return binding;
        }

        private IDisposable NotItems(MemberAccessor member)
        {
            engine.Report(target.Description, spec, $"{member.Name} is not a collection.");
            target.Reset(ReadOnlySpan<object>.Empty);
            return Disposable.Empty;
        }
    }
}
