#pragma warning disable VSTHRD200 // Use "Async" suffix for async methods

using AlterNats.Internal;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;

namespace AlterNats.Commands;

internal abstract class CommandBase<TSelf> : ICommand, IObjectPoolNode<TSelf>
    where TSelf : class, IObjectPoolNode<TSelf>
{
    TSelf? next;
    public ref TSelf? NextNode => ref next;

    static readonly Action<object?> cancelAction = SetCancel;
    CancellationTokenRegistration timerRegistration;
    CancellationTimer? timer;
    public bool IsCanceled { get; private set; }

    protected abstract void Reset();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool TryRent(ObjectPool pool, [NotNullWhen(true)] out TSelf? self)
    {
        return pool.TryRent<TSelf>(out self!);
    }

    void ICommand.Return(ObjectPool pool)
    {
        timerRegistration.Dispose(); // wait for cancel callback complete
        timerRegistration = default;

        // if failed to return timer, maybe invoked timer callback so avoid race condition, does not return command itself to pool.
        if (!IsCanceled && (timer == null || timer.TryReturn()))
        {
            timer = null;
            Reset();
            pool.Return(Unsafe.As<TSelf>(this));
        }
    }

    public abstract void Write(ProtocolWriter writer);

    public void SetCancellationTimer(CancellationTimer timer)
    {
        this.timer = timer;
        this.timerRegistration = timer.Token.UnsafeRegister(cancelAction, this);
    }

    static void SetCancel(object? state)
    {
        var self = (CommandBase<TSelf>)state!;
        self.IsCanceled = true;
    }
}

internal abstract class AsyncCommandBase<TSelf> : ICommand, IAsyncCommand, IObjectPoolNode<TSelf>, IValueTaskSource, IPromise, IThreadPoolWorkItem
    where TSelf : class, IObjectPoolNode<TSelf>
{
    TSelf? next;
    public ref TSelf? NextNode => ref next;

    static readonly Action<object?> cancelAction = SetCancel;
    CancellationTokenRegistration timerRegistration;
    CancellationTimer? timer;
    public bool IsCanceled { get; private set; }

    ObjectPool? objectPool;
    bool noReturn;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool TryRent(ObjectPool pool, [NotNullWhen(true)] out TSelf? self)
    {
        return pool.TryRent<TSelf>(out self!);
    }

    ManualResetValueTaskSourceCore<object> core;

    void ICommand.Return(ObjectPool pool)
    {
        // don't return manually, only allows from await.
        // however, set pool on this timing.
        objectPool = pool;
    }

    public abstract void Write(ProtocolWriter writer);

    protected abstract void Reset();

    public ValueTask AsValueTask()
    {
        return new ValueTask(this, core.Version);
    }

    public void SetResult()
    {
        // succeed operation, remove canceler
        timerRegistration.Dispose();
        timerRegistration = default;

        if (IsCanceled) return; // already called Canceled, it invoked SetCanceled.

        if (timer != null)
        {
            if (!timer.TryReturn())
            {
                // cancel is called. don't set result.
                return;
            }
            timer = null;
        }

        ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
    }

    public void SetCanceled(CancellationToken cancellationToken)
    {
        if (noReturn) return;

        timerRegistration.Dispose();
        timerRegistration = default;

        noReturn = true;
        ThreadPool.UnsafeQueueUserWorkItem(state =>
        {
            state.self.core.SetException(new OperationCanceledException(state.cancellationToken));
        }, (self: this, cancellationToken), preferLocal: false);
    }

    public void SetException(Exception exception)
    {
        if (noReturn) return;

        timerRegistration.Dispose();
        timerRegistration = default;

        noReturn = true;
        ThreadPool.UnsafeQueueUserWorkItem(state =>
        {
            state.self.core.SetException(state.exception);
        }, (self: this, exception), preferLocal: false);
    }

    void IValueTaskSource.GetResult(short token)
    {
        try
        {
            core.GetResult(token);
        }
        finally
        {
            core.Reset();
            Reset();
            var p = objectPool;
            objectPool = null;
            timer = null;
            timerRegistration = default;
            if (p != null && !noReturn) // canceled object don't return pool to avoid call SetResult/Exception after await
            {
                p.Return(Unsafe.As<TSelf>(this));
            }
        }
    }

    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token)
    {
        return core.GetStatus(token);
    }

    void IValueTaskSource.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        core.OnCompleted(continuation, state, token, flags);
    }

    void IThreadPoolWorkItem.Execute()
    {
        core.SetResult(null!);
    }

    public void SetCancellationTimer(CancellationTimer timer)
    {
        this.timer = timer;
        this.timerRegistration = timer.Token.UnsafeRegister(cancelAction, this);
    }

    static void SetCancel(object? state)
    {
        var self = (AsyncCommandBase<TSelf>)state!;
        self.IsCanceled = true;
        var token = self.timer?.GetCanceledToken() ?? CancellationToken.None;
        self.SetCanceled(token);
    }
}

internal abstract class AsyncCommandBase<TSelf, TResponse> : ICommand, IAsyncCommand<TResponse>, IObjectPoolNode<TSelf>, IValueTaskSource<TResponse>, IPromise, IPromise<TResponse>, IThreadPoolWorkItem
    where TSelf : class, IObjectPoolNode<TSelf>
{
    TSelf? next;
    public ref TSelf? NextNode => ref next;

    static readonly Action<object?> cancelAction = SetCancel;
    CancellationTokenRegistration timerRegistration;
    CancellationTimer? timer;
    public bool IsCanceled { get; private set; }

    ManualResetValueTaskSourceCore<TResponse> core;
    TResponse? response;
    ObjectPool? objectPool;
    bool noReturn;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool TryRent(ObjectPool pool, [NotNullWhen(true)] out TSelf? self)
    {
        return pool.TryRent<TSelf>(out self!);
    }

    public ValueTask<TResponse> AsValueTask()
    {
        return new ValueTask<TResponse>(this, core.Version);
    }

    void ICommand.Return(ObjectPool pool)
    {
        // don't return manually, only allows from await.
        // however, set pool on this timing.
        objectPool = pool;
    }

    public abstract void Write(ProtocolWriter writer);

    protected abstract void Reset();

    void IPromise.SetResult()
    {
        // called when SocketWriter.Flush, however continuation should run on response received.
    }

    public void SetResult(TResponse result)
    {
        response = result;

        if (IsCanceled) return; // already called Canceled, it invoked SetCanceled.

        timerRegistration.Dispose();
        timerRegistration = default;

        if (timer != null && objectPool != null)
        {
            if (!timer.TryReturn())
            {
                // cancel is called. don't set result.
                return;
            }
            timer = null;
        }

        ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
    }

    public void SetCanceled(CancellationToken cancellationToken)
    {
        if (noReturn) return;

        timerRegistration.Dispose();
        timerRegistration = default;

        noReturn = true;
        ThreadPool.UnsafeQueueUserWorkItem(state =>
        {
            state.self.core.SetException(new OperationCanceledException(state.cancellationToken));
        }, (self: this, cancellationToken), preferLocal: false);
    }

    public void SetException(Exception exception)
    {
        if (noReturn) return;

        timerRegistration.Dispose();
        timerRegistration = default;

        noReturn = true;
        ThreadPool.UnsafeQueueUserWorkItem(state =>
        {
            state.self.core.SetException(state.exception);
        }, (self: this, exception), preferLocal: false);
    }

    TResponse IValueTaskSource<TResponse>.GetResult(short token)
    {
        try
        {
            return core.GetResult(token);
        }
        finally
        {
            core.Reset();
            Reset();
            var p = objectPool;
            response = default!;
            objectPool = null;
            timer = null;
            timerRegistration = default;
            if (p != null && !noReturn) // canceled object don't return pool to avoid call SetResult/Exception after await
            {
                p.Return(Unsafe.As<TSelf>(this));
            }
        }
    }

    ValueTaskSourceStatus IValueTaskSource<TResponse>.GetStatus(short token)
    {
        return core.GetStatus(token);
    }

    void IValueTaskSource<TResponse>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        core.OnCompleted(continuation, state, token, flags);
    }

    void IThreadPoolWorkItem.Execute()
    {
        core.SetResult(response!);
    }

    public void SetCancellationTimer(CancellationTimer timer)
    {
        this.timer = timer;
        this.timerRegistration = timer.Token.UnsafeRegister(cancelAction, this);
    }

    static void SetCancel(object? state)
    {
        var self = (AsyncCommandBase<TSelf, TResponse>)state!;
        self.IsCanceled = true;
        var token = self.timer?.GetCanceledToken() ?? CancellationToken.None;
        self.SetCanceled(token);
    }
}
