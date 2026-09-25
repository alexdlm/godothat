using R3;

namespace GodotHat.Binding;

// The owner currently observed by a MemberSwitch, so two-way bindings know where to write back.
internal sealed class OwnerTracker
{
    private object? owner;

    public object? Owner
    {
        get => Volatile.Read(ref this.owner);
        set => Volatile.Write(ref this.owner, value);
    }
}

// Observes a member of each owner in turn, like Select(member.Observe).Switch(), but a null owner, a reactive property
// that completes (as R3 properties do when disposed), a failed source or one that is already disposed becomes Missing
// rather than being swallowed (R3's Switch hides inner completion). That releases the owner, so a freed node view
// model isn't kept alive by bindings to it. Other sources, such as constants, keep their last value when they
// complete. The result never completes by itself; bindings end when disposed.
internal sealed class MemberSwitch<T>(Observable<object?> owners, ValueMember<T> member, OwnerTracker? tracker)
    : Observable<BindingValue<T>>
{
    protected override IDisposable SubscribeCore(Observer<BindingValue<T>> observer) =>
        owners.Subscribe(new OwnerObserver(observer, member, tracker));

    private sealed class OwnerObserver(
        Observer<BindingValue<T>> observer,
        ValueMember<T> member,
        OwnerTracker? tracker) : Observer<object?>
    {
        private readonly object gate = new();
        private SerialDisposableCore inner;
        private int generation;

        protected override bool AutoDisposeOnCompleted => false;

        protected override void OnNextCore(object? owner)
        {
            int current;
            lock (this.gate)
            {
                current = ++this.generation;
                tracker?.Owner = owner;
            }

            this.inner.Disposable = null;

            if (owner is null)
            {
                this.Emit(current, BindingValue<T>.Missing);
                return;
            }

            try
            {
                Observable<T> source = member.Observe(owner);
                this.inner.Disposable = source.Subscribe(new MemberObserver(this, current));
            }
            catch (ObjectDisposedException)
            {
                this.Dead(current, null);
            }
        }

        protected override void OnErrorResumeCore(Exception error) => observer.OnErrorResume(error);

        protected override void OnCompletedCore(Result result)
        {
            // The owners completing (eg a constant context) doesn't end the member being observed.
            if (result.IsFailure)
            {
                observer.OnErrorResume(result.Exception);
            }
        }

        protected override void DisposeCore() => this.inner.Dispose();

        private void Emit(int from, BindingValue<T> value)
        {
            lock (this.gate)
            {
                if (from == this.generation)
                {
                    observer.OnNext(value);
                }
            }
        }

        private void Error(int from, Exception error)
        {
            lock (this.gate)
            {
                if (from == this.generation)
                {
                    observer.OnErrorResume(error);
                }
            }
        }

        private void Completed(int from, Result result)
        {
            if (result.IsFailure || member.Kind is MemberKind.ReactiveProperty or MemberKind.ReadOnlyProperty)
            {
                this.Dead(from, result.Exception);
            }
        }

        private void Dead(int from, Exception? error)
        {
            lock (this.gate)
            {
                if (from != this.generation)
                {
                    return;
                }

                if (tracker is not null)
                {
                    tracker.Owner = null;
                }

                if (error is not null)
                {
                    observer.OnErrorResume(error);
                }

                observer.OnNext(BindingValue<T>.Missing);
            }
        }

        private sealed class MemberObserver(OwnerObserver parent, int generation) : Observer<T>
        {
            protected override void OnNextCore(T value) => parent.Emit(generation, new BindingValue<T>(value));

            protected override void OnErrorResumeCore(Exception error) => parent.Error(generation, error);

            protected override void OnCompletedCore(Result result) => parent.Completed(generation, result);
        }
    }
}
