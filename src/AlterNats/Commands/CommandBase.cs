using AlterNats.Internal;
using System.Runtime.CompilerServices;
using System.Threading.Tasks.Sources;

namespace AlterNats.Commands;

internal abstract class CommandBase<TSelf> : ICommand, IObjectPoolNode<TSelf>
    where TSelf : class, IObjectPoolNode<TSelf>
{
    protected static ObjectPool<TSelf> pool;

    TSelf? nextNode;
    public ref TSelf? NextNode => ref nextNode;

    public abstract void Reset();

    void ICommand.Return()
    {
        Reset();
        pool.TryPush(Unsafe.As<TSelf>(this));
    }

    public abstract void Write(ProtocolWriter writer);
}

internal abstract class AsyncCommandBase<TSelf> : ICommand, IObjectPoolNode<TSelf>, IValueTaskSource, IPromise, IThreadPoolWorkItem
    where TSelf : class, IObjectPoolNode<TSelf>
{
    protected static ObjectPool<TSelf> pool;

    TSelf? nextNode;
    public ref TSelf? NextNode => ref nextNode;

    ManualResetValueTaskSourceCore<object> core;

    void ICommand.Return()
    {
        // don't return manually, only allows from await.
    }

    public abstract void Write(ProtocolWriter writer);

    public abstract void Reset();


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
            pool.TryPush(Unsafe.As<TSelf>(this));
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
