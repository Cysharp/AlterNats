namespace AlterNats.Internal;

// Support efficiently cancellation support for connection-dispose/timeout/cancel-per-operation

internal sealed class CancellationTimerPool
{
    readonly ObjectPool pool;
    readonly CancellationToken rootToken;

    public CancellationTimerPool(ObjectPool pool, CancellationToken rootToken)
    {
        this.pool = pool;
        this.rootToken = rootToken;
    }

    public CancellationTimer Start(TimeSpan timeout, CancellationToken externalCancellationToken)
    {
        return CancellationTimer.Start(pool, rootToken, timeout, externalCancellationToken);
    }
}

internal sealed class CancellationTimer : IObjectPoolNode<CancellationTimer>
{
    // timer itself is ObjectPool Node
    CancellationTimer? next;
    public ref CancellationTimer? NextNode => ref next;

    // underyling source
    readonly CancellationTokenSource cancellationTokenSource;
    readonly ObjectPool pool;

    bool calledExternalTokenCancel;
    TimeSpan timeout;
    CancellationToken rootToken;
    CancellationToken externalCancellationToken;
    CancellationTokenRegistration externalTokenRegistration;

    public CancellationToken Token => cancellationTokenSource.Token;

    public Exception GetExceptionWhenCanceled()
    {
        if (rootToken.IsCancellationRequested)
        {
            return new NatsException("Operation is canceled because connection is disposed.");
        }
        if (externalCancellationToken.IsCancellationRequested)
        {
            return new OperationCanceledException(externalCancellationToken);
        }
        return new TimeoutException($"Nats operation is canceled due to the configured timeout of {timeout.TotalSeconds} seconds elapsing.");
    }

    // this timer pool is tightly coupled with rootToken lifetime(e.g. connection lifetime).
    public CancellationTimer(ObjectPool pool, CancellationToken rootToken)
    {
        this.pool = pool;
        this.rootToken = rootToken;
        this.cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(rootToken);
    }

    public static CancellationTimer Start(ObjectPool pool, CancellationToken rootToken, TimeSpan timeout, CancellationToken externalCancellationToken)
    {
        if (!pool.TryRent<CancellationTimer>(out var self))
        {
            self = new CancellationTimer(pool, rootToken);
        }

        // Timeout with external cancellationToken
        self.externalCancellationToken = externalCancellationToken;
        if (externalCancellationToken.CanBeCanceled)
        {
            self.externalTokenRegistration = externalCancellationToken.UnsafeRegister(static state =>
            {
                var self = ((CancellationTimer)state!);
                self.calledExternalTokenCancel = true;
                self.cancellationTokenSource.Cancel();
            }, self);
        }

        self.timeout = timeout;
        self.cancellationTokenSource.CancelAfter(timeout);
        return self;
    }

    // We can check cancel is called(calling) by return value
    public bool TryReturn()
    {
        if (externalTokenRegistration.Token.CanBeCanceled)
        {
            var notCancelRaised = externalTokenRegistration.Unregister();
            if (!notCancelRaised)
            {
                // may invoking CancellationTokenSource.Cancel so don't call .Dispose.
                return false;
            }
        }

        // if timer is not raised, successful reset so ok to return pool
        if (cancellationTokenSource.TryReset())
        {
            calledExternalTokenCancel = false;
            externalCancellationToken = default;
            externalTokenRegistration = default;
            timeout = TimeSpan.Zero;

            pool.Return(this);
            return true;
        }
        else
        {
            // otherwise, don't reuse.
            cancellationTokenSource.Cancel();
            cancellationTokenSource.Dispose();
            return false;
        }
    }
}
