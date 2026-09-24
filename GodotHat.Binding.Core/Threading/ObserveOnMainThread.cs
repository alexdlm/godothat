using R3;

namespace GodotHat.Binding;

internal static class MainThreadObservableExtensions
{
    // Delivers on the main thread: immediately when the value arrives there and nothing is queued (so order is kept),
    // otherwise through the dispatcher. With coalesce, only the latest value is kept and it is always posted, so a
    // burst within a frame becomes one update. Errors and completion are delivered on the main thread too.
    public static Observable<T> ObserveOnMainThread<T>(this Observable<T> source, BindingDispatcher dispatcher, bool coalesce) =>
        new ObserveOnMainThread<T>(source, dispatcher, coalesce);
}

internal sealed class ObserveOnMainThread<T>(Observable<T> source, BindingDispatcher dispatcher, bool coalesce)
    : Observable<T>
{
    protected override IDisposable SubscribeCore(Observer<T> observer) =>
        source.Subscribe(new Sink(observer, dispatcher, coalesce));

    private sealed class Sink(Observer<T> observer, BindingDispatcher dispatcher, bool coalesce)
        : Observer<T>, IDispatchWork
    {
        private readonly object gate = new();
        private Queue<Notification<T>>? queue;
        private T latest = default!;
        private bool hasLatest;
        private Result? completion;
        private bool posted;

        protected override bool AutoDisposeOnCompleted => false;

        protected override void OnNextCore(T value)
        {
            bool post;
            lock (this.gate)
            {
                if (!coalesce && !this.posted && dispatcher.IsMainThread)
                {
                    post = false;
                }
                else
                {
                    if (coalesce)
                    {
                        this.latest = value;
                        this.hasLatest = true;
                    }
                    else
                    {
                        (this.queue ??= new Queue<Notification<T>>()).Enqueue(new Notification<T>(value));
                    }

                    this.PostLocked(out post);
                    if (post)
                    {
                        dispatcher.Post(this);
                    }

                    return;
                }
            }

            observer.OnNext(value);
        }

        protected override void OnErrorResumeCore(Exception error)
        {
            bool post;
            lock (this.gate)
            {
                if (!this.posted && dispatcher.IsMainThread)
                {
                    post = false;
                }
                else
                {
                    this.queue ??= new Queue<Notification<T>>();

                    // Keep a coalesced value that arrived first ahead of the error
                    if (this.hasLatest)
                    {
                        this.queue.Enqueue(new Notification<T>(this.latest));
                        this.latest = default!;
                        this.hasLatest = false;
                    }

                    this.queue.Enqueue(new Notification<T>(error));
                    this.PostLocked(out post);
                    if (post)
                    {
                        dispatcher.Post(this);
                    }

                    return;
                }
            }

            observer.OnErrorResume(error);
        }

        protected override void OnCompletedCore(Result result)
        {
            bool post;
            lock (this.gate)
            {
                if (!this.posted && dispatcher.IsMainThread)
                {
                    post = false;
                }
                else
                {
                    this.completion = result;
                    this.PostLocked(out post);
                    if (post)
                    {
                        dispatcher.Post(this);
                    }

                    return;
                }
            }

            observer.OnCompleted(result);
            this.Dispose();
        }

        private void PostLocked(out bool post)
        {
            post = !this.posted;
            this.posted = true;
        }

        public void Run()
        {
            while (!this.IsDisposed)
            {
                Notification<T> next = default;
                bool hasNext = true;
                Result? done = null;
                lock (this.gate)
                {
                    if (this.queue is { Count: > 0 })
                    {
                        next = this.queue.Dequeue();
                    }
                    else if (this.hasLatest)
                    {
                        next = new Notification<T>(this.latest);
                        this.latest = default!;
                        this.hasLatest = false;
                    }
                    else
                    {
                        hasNext = false;
                        this.posted = false;
                        done = this.completion;
                        this.completion = null;
                    }
                }

                if (!hasNext)
                {
                    if (done is { } result)
                    {
                        observer.OnCompleted(result);
                        this.Dispose();
                    }

                    return;
                }

                if (next.Kind == NotificationKind.OnNext)
                {
                    observer.OnNext(next.Value);
                }
                else
                {
                    observer.OnErrorResume(next.Error);
                }
            }
        }
    }
}
