using R3;

namespace GodotHat.Binding;

public sealed partial class BindingEngine
{
    private sealed class EventBinding<TIn> : IConverterContinuation<TIn>, IDisposable
    {
        private readonly BindingEngine engine;
        private readonly BindingSpec spec;
        private readonly IEventTarget target;
        private IDisposable? subscription;

        public EventBinding(BindingEngine engine, BindingSpec spec, IEventTarget target)
        {
            this.engine = engine;
            this.spec = spec;
            this.target = target;
        }

        public void Start(Observable<BindingValue<TIn>> source) =>
            this.engine.ApplyConverter(this.spec, this.target.Description, source, this);

        public void Continue<TOut>(Observable<BindingValue<TOut>> converted, TryConvertBack<TOut, TIn>? convertBack)
        {
            if (!this.target.CanAccept<TOut>(out string? error))
            {
                this.engine.Report(this.target.Description, this.spec, error);
                return;
            }

            this.subscription = converted
                .ObserveOnMainThread(this.engine.Dispatcher, coalesce: false)
                .Subscribe(new EventObserver<TOut>(this));
        }

        public void Dispose() => this.subscription?.Dispose();

        private sealed class EventObserver<TOut>(EventBinding<TIn> binding) : Observer<BindingValue<TOut>>
        {
            protected override void OnNextCore(BindingValue<TOut> value)
            {
                if (!value.HasValue)
                {
                    return;
                }

                try
                {
                    binding.target.Invoke(value.Value);
                }
                catch (Exception e)
                {
                    binding.engine.Report(binding.target.Description, binding.spec, "The event target failed.", e);
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
        }
    }
}
