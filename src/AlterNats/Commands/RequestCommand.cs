using AlterNats.Internal;
using System.Threading.Tasks.Sources;

namespace AlterNats.Commands;

// similar as AsyncCommandBase
internal sealed class RequestAsyncCommand<TRequest, TResponse> : ICommand, IObjectPoolNode<RequestAsyncCommand<TRequest, TResponse>>, IValueTaskSource<TResponse?>, IPromise, IPromise<TResponse>, IThreadPoolWorkItem
{
    static ObjectPool<RequestAsyncCommand<TRequest, TResponse>> pool;

    RequestAsyncCommand<TRequest, TResponse>? nextNode;
    public ref RequestAsyncCommand<TRequest, TResponse>? NextNode => ref nextNode;

    ManualResetValueTaskSourceCore<TResponse?> core;
    TResponse? response;
    readonly Action<TResponse> setResultAction;

    RequestAsyncCommand()
    {
        setResultAction = SetResult;
    }

    public static RequestAsyncCommand<TRequest, TResponse> Create()
    {
        if (!pool.TryPop(out var result))
        {
            result = new RequestAsyncCommand<TRequest, TResponse>();
        }

        // TODO:setup field

        return result;
    }

    public ValueTask<TResponse?> AsValueTask()
    {
        return new ValueTask<TResponse?>(this, core.Version);
    }

    void IThreadPoolWorkItem.Execute()
    {
        core.SetResult(response);
    }

    TResponse? IValueTaskSource<TResponse?>.GetResult(short token)
    {
        return core.GetResult(token);
    }

    ValueTaskSourceStatus IValueTaskSource<TResponse?>.GetStatus(short token)
    {
        return core.GetStatus(token);
    }

    void IValueTaskSource<TResponse?>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        core.OnCompleted(continuation, state, token, flags);
    }

    void ICommand.Return()
    {
        // don't return manually, only allows from await.
    }

    void IPromise.SetCanceled(CancellationToken cancellationToken)
    {
        ThreadPool.UnsafeQueueUserWorkItem(state =>
        {
            state.self.core.SetException(new OperationCanceledException(state.cancellationToken));
        }, (self: this, cancellationToken), preferLocal: false);
    }

    void IPromise.SetException(Exception exception)
    {
        ThreadPool.UnsafeQueueUserWorkItem(state =>
        {
            state.self.core.SetException(state.exception);
        }, (self: this, exception), preferLocal: false);
    }

    void IPromise.SetResult()
    {
        // called when SocketWriter.Flush, however continuation should run on response received.
    }

    void ICommand.Write(ProtocolWriter writer)
    {
        // TODO:not yet implemented.
        //throw new NotImplementedException();

        // writer.WritePublish(
    }

    public Action<TResponse> GetSetResultAction()
    {
        return setResultAction;
    }

    public void SetResult(TResponse result)
    {

        // TODO:not yet implemented.
        throw new NotImplementedException();
    }
}
