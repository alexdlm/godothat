using R3;

namespace GodotHat.Binding;

public sealed partial class BindingEngine
{
    private bool ApplyConverter<TIn>(
        BindingSpec spec,
        string target,
        Observable<BindingValue<TIn>> source,
        IConverterContinuation<TIn> continuation)
    {
        if (string.IsNullOrEmpty(spec.Converter))
        {
            continuation.Continue(source, Identity<TIn>.Back);
            return true;
        }

        if (!this.Converters.TryGet(spec.Converter, out IBindingConverter? converter))
        {
            this.Report(target, spec, $"Unknown converter '{spec.Converter}'.");
            return false;
        }

        try
        {
            if (!converter.TryApply(source, spec.ConverterArgument, continuation, out string? error))
            {
                this.Report(target, spec, error);
                return false;
            }
        }
        catch (Exception e)
        {
            this.Report(target, spec, $"Converter '{spec.Converter}' failed.", e);
            return false;
        }

        return true;
    }

    private static class Identity<T>
    {
        public static readonly TryConvertBack<T, T> Back = static (T value, out T result) =>
        {
            result = value;
            return true;
        };
    }

    private sealed class PropertyBinding<TIn> : IConverterContinuation<TIn>, IDisposable
    {
        private readonly BindingEngine engine;
        private readonly BindingSpec spec;
        private readonly IBindingTarget target;
        private readonly ValueMember<TIn> member;
        private readonly OwnerTracker owner = new();
        private IDisposable? forward;
        private IDisposable? changes;

        public PropertyBinding(BindingEngine engine, BindingSpec spec, IBindingTarget target, ValueMember<TIn> member)
        {
            this.engine = engine;
            this.spec = spec;
            this.target = target;
            this.member = member;
        }

        public void Start(Observable<object?> owners)
        {
            if (this.spec.Mode == BindingMode.TwoWay && !this.member.CanWrite)
            {
                this.engine.Report(this.target.Description, this.spec, $"{this.member.Name} is read-only, so the binding is one-way.");
            }

            var source = new MemberSwitch<TIn>(owners, this.member, this.owner);
            if (!this.engine.ApplyConverter(this.spec, this.target.Description, source, this))
            {
                this.target.SetFallback();
            }
        }

        public void Continue<TOut>(Observable<BindingValue<TOut>> converted, TryConvertBack<TOut, TIn>? convertBack)
        {
            if (!this.target.CanAccept<TOut>(out string? error))
            {
                this.engine.Report(this.target.Description, this.spec, error);
                this.target.SetFallback();
                return;
            }

            var observer = new TargetObserver<TOut>(this, convertBack);
            this.forward = converted
                .DistinctUntilChanged()
                .ObserveOnMainThread(this.engine.Dispatcher, this.spec.Coalesce)
                .Subscribe(observer);

            if (this.spec.Mode != BindingMode.TwoWay || !this.member.CanWrite)
            {
                return;
            }

            if (convertBack is null)
            {
                this.engine.Report(this.target.Description, this.spec, $"Converter '{this.spec.Converter}' can't convert back, so the binding is one-way.");
                return;
            }

            this.changes = this.target.ObserveChanges(observer.OnTargetChanged);
            if (this.changes is null)
            {
                this.engine.Report(this.target.Description, this.spec, "The this.target has no change notification, so the binding is one-way.");
            }
        }

        public void Dispose()
        {
            this.changes?.Dispose();
            this.forward?.Dispose();
        }

        private sealed class TargetObserver<TOut>(PropertyBinding<TIn> binding, TryConvertBack<TOut, TIn>? convertBack)
            : Observer<BindingValue<TOut>>
        {
            // Set while this binding writes to the target or source, so the change it causes isn't echoed back.
            private bool writingTarget;
            private bool writingSource;

            protected override void OnNextCore(BindingValue<TOut> value)
            {
                if (this.writingSource)
                {
                    return;
                }

                this.writingTarget = true;
                try
                {
                    if (value.HasValue)
                    {
                        binding.target.SetValue(value.Value);
                    }
                    else
                    {
                        binding.target.SetFallback();
                    }
                }
                catch (Exception e)
                {
                    binding.engine.Report(binding.target.Description, binding.spec, "Failed to apply the value.", e);
                }
                finally
                {
                    this.writingTarget = false;
                }

                if (value.HasValue && binding.spec.Mode == BindingMode.OneTime)
                {
                    this.Dispose();
                }
            }

            protected override void OnErrorResumeCore(Exception error) =>
                binding.engine.Report(binding.target.Description, binding.spec, "The source failed.", error);

            protected override void OnCompletedCore(Result result)
            {
                if (result.IsFailure)
                {
                    this.OnErrorResumeCore(result.Exception);
                }
            }

            public void OnTargetChanged()
            {
                if (this.writingTarget || this.IsDisposed || convertBack is null)
                {
                    return;
                }

                if (!binding.target.TryReadValue(out TOut targetValue) || !convertBack(targetValue, out TIn sourceValue))
                {
                    return;
                }

                if (binding.owner.Owner is not { } owner)
                {
                    return;
                }

                this.writingSource = true;
                try
                {
                    binding.member.Write(owner, sourceValue);
                }
                catch (Exception e)
                {
                    binding.engine.Report(binding.target.Description, binding.spec, "Failed to write the value back.", e);
                }
                finally
                {
                    this.writingSource = false;
                }
            }
        }
    }
}
