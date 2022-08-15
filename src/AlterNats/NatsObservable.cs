namespace AlterNats;

internal sealed class NatsObservable<T> : IObservable<T>
{
    readonly NatsConnection connection;
    readonly NatsKey key;

    public NatsObservable(NatsConnection connection, in NatsKey key)
    {
        this.key = key;
        this.connection = connection;
    }

    public IDisposable Subscribe(IObserver<T> observer)
    {
        var disp = new CancellationTokenDisposable();
        var disp2 = new FireAndForgetDisposable(connection.SubscribeAsync<T>(key, observer.OnNext, disp.Token), observer.OnError);
        return new Tuple2Disposable(disp, disp2);
    }

    sealed class FireAndForgetDisposable : IDisposable
    {
        bool disposed;
        IDisposable? taskDisposable;
        Action<Exception> onError;
        object gate = new object();

        public FireAndForgetDisposable(ValueTask<IDisposable> task, Action<Exception> onError)
        {
            this.disposed = false;
            this.onError = onError;
            FireAndForget(task);
        }

        async void FireAndForget(ValueTask<IDisposable> task)
        {
            try
            {
                taskDisposable = await task.ConfigureAwait(false);
                lock (gate)
                {
                    if (disposed)
                    {
                        taskDisposable.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                lock (gate)
                {
                    disposed = true;
                }
                onError(ex);
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                if (taskDisposable != null)
                {
                    taskDisposable.Dispose();
                }
            }
        }
    }

    internal class CancellationTokenDisposable : IDisposable
    {
        bool disposed;
        readonly CancellationTokenSource cancellationTokenSource;

        public CancellationToken Token => cancellationTokenSource.Token;

        public CancellationTokenDisposable()
        {
            this.cancellationTokenSource = new CancellationTokenSource();
        }

        public void Dispose()
        {
            if (!disposed)
            {
                disposed = true;
                cancellationTokenSource.Cancel();
                cancellationTokenSource.Dispose();
            }
        }
    }

    internal class Tuple2Disposable : IDisposable
    {
        readonly IDisposable disposable1;
        readonly IDisposable disposable2;

        public Tuple2Disposable(IDisposable disposable1, IDisposable disposable2)
        {
            this.disposable1 = disposable1;
            this.disposable2 = disposable2;
        }

        public void Dispose()
        {
            this.disposable1.Dispose();
            this.disposable2.Dispose();
        }
    }
}
