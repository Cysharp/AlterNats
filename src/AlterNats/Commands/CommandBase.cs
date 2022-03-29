using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;

namespace AlterNats.Commands;

internal abstract class CommandBase<TSelf> : ICommand
    where TSelf : class
{
    static int count; // approximately count
    static readonly ConcurrentQueue<TSelf> pool = new();

    internal static int GetCacheCount => count;

    protected abstract void Reset();

    protected static bool TryRent([NotNullWhen(true)] out TSelf? self)
    {
        if (pool.TryDequeue(out self!))
        {
            Interlocked.Decrement(ref count);
            return true;
        }
        else
        {
            self = default;
            return false;
        }
    }

    void ICommand.Return()
    {
        Reset();
        if (count < NatsConnection.MaxCommandCacheSize)
        {
            pool.Enqueue(Unsafe.As<TSelf>(this));
            Interlocked.Increment(ref count);
        }
    }

    public abstract void Write(ProtocolWriter writer);
}

internal abstract class AsyncCommandBase<TSelf> : ICommand, IValueTaskSource, IPromise, IThreadPoolWorkItem
    where TSelf : class
{
    static int count; // approximately count
    static readonly ConcurrentQueue<TSelf> pool = new();

    internal static int GetCacheCount => count;

    protected static bool TryRent([NotNullWhen(true)] out TSelf? self)
    {
        if (pool.TryDequeue(out self!))
        {
            Interlocked.Decrement(ref count);
            return true;
        }
        else
        {
            self = default;
            return false;
        }
    }

    ManualResetValueTaskSourceCore<object> core;

    void ICommand.Return()
    {
        // don't return manually, only allows from await.
    }

    public abstract void Write(ProtocolWriter writer);

    protected abstract void Reset();


    public ValueTask AsValueTask()
    {
        return new ValueTask(this, core.Version);
    }

    public virtual void SetResult()
    {
        ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
    }

    public void SetCanceled(CancellationToken cancellationToken)
    {
        ThreadPool.UnsafeQueueUserWorkItem(state =>
        {
            state.self.core.SetException(new OperationCanceledException(state.cancellationToken));
        }, (self: this, cancellationToken), preferLocal: false);
    }

    public void SetException(Exception exception)
    {
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
            if (count < NatsConnection.MaxCommandCacheSize)
            {
                pool.Enqueue(Unsafe.As<TSelf>(this));
                Interlocked.Increment(ref count);
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
}
