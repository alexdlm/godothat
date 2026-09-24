using System.Globalization;
using System.Windows.Input;
using R3;

namespace GodotHat.Binding;

public sealed partial class BindingEngine
{
    private sealed class CommandBinding<TParam>(
        BindingEngine engine,
        BindingSpec spec,
        ICommandTarget target,
        CommandMember<TParam> member) : IDisposable
    {
        private readonly OwnerTracker owner = new();
        private readonly CompositeDisposable subscriptions = new();
        private TParam constant = default!;
        private object? item;

        public void Start(Observable<object?> owners, DataContext context)
        {
            if (spec.Parameter == BindingParameterKind.Constant &&
                !ValueParser.TryParse(spec.ParameterValue, CultureInfo.InvariantCulture, out this.constant))
            {
                engine.Report(target.Description, spec, $"Parameter '{spec.ParameterValue}' is not a {typeof(TParam).Name}.");
                target.SetAvailable(false);
                return;
            }

            if (spec.Parameter == BindingParameterKind.Item)
            {
                this.subscriptions.Add(context.Values.Subscribe(this, static (value, binding) => Volatile.Write(ref binding.item, value)));
            }

            this.subscriptions.Add(
                owners
                    .Select(this, static (owner, binding) => binding.ObserveAvailability(owner))
                    .Switch()
                    .ObserveOnMainThread(engine.Dispatcher, coalesce: false)
                    .Subscribe(target, static (available, t) => t.SetAvailable(available)));

            this.subscriptions.Add(target.ObserveInvoked(this.Execute));
        }

        public void Dispose() => this.subscriptions.Dispose();

        private Observable<bool> ObserveAvailability(object? commandOwner)
        {
            this.owner.Owner = commandOwner;
            return commandOwner is null
                ? Observable.Return(false)
                : new CanExecuteObservable(member.GetCommand(commandOwner));
        }

        private void Execute()
        {
            if (this.owner.Owner is not { } commandOwner || !member.GetCommand(commandOwner).CanExecute(null))
            {
                return;
            }

            TParam parameter;
            switch (spec.Parameter)
            {
                case BindingParameterKind.Item:
                    object? current = Volatile.Read(ref this.item);
                    if (current is not TParam typed)
                    {
                        engine.Report(target.Description, spec, $"The item ({current?.GetType().Name ?? "null"}) is not a {typeof(TParam).Name}.");
                        return;
                    }

                    parameter = typed;
                    break;
                case BindingParameterKind.Constant:
                    parameter = this.constant;
                    break;
                default:
                    parameter = default!;
                    break;
            }

            try
            {
                member.Execute(commandOwner, parameter);
            }
            catch (Exception e)
            {
                engine.Report(target.Description, spec, "The command failed.", e);
            }
        }
    }

    // A command's availability: its current CanExecute on subscribe, then on each CanExecuteChanged.
    private sealed class CanExecuteObservable(ICommand command) : Observable<bool>
    {
        protected override IDisposable SubscribeCore(Observer<bool> observer)
        {
            EventHandler handler = (_, _) => observer.OnNext(command.CanExecute(null));
            command.CanExecuteChanged += handler;
            observer.OnNext(command.CanExecute(null));
            return Disposable.Create((command, handler), static state => state.command.CanExecuteChanged -= state.handler);
        }
    }
}
