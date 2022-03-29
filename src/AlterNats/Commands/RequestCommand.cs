using AlterNats.Internal;
using System.Collections.Concurrent;
using System.Threading.Tasks.Sources;

namespace AlterNats.Commands;

// similar as AsyncCommandBase
// SetResult(T) -> ThreadPoolItem.Execute -> continuation.

internal sealed class RequestAsyncCommand<TRequest, TResponse> : ICommand, IValueTaskSource<TResponse?>, IPromise, IPromise<TResponse>, IThreadPoolWorkItem
{
    static int count; // approximately count
    static readonly ConcurrentQueue<RequestAsyncCommand<TRequest, TResponse>> pool = new();

    ManualResetValueTaskSourceCore<TResponse?> core;
    string? stringKey;
    NatsKey? natsKey;
    TRequest? request;
    TResponse? response;
    ReadOnlyMemory<byte> inboxPrefix;
    int id;

    RequestAsyncCommand()
    {
    }

    public static RequestAsyncCommand<TRequest, TResponse> Create(string key, ReadOnlyMemory<byte> inboxPrefix, int id, TRequest request)
    {
        if (!pool.TryDequeue(out var result))
        {
            Interlocked.Decrement(ref count);
            result = new RequestAsyncCommand<TRequest, TResponse>();
        }

        result.stringKey = key;
        result.inboxPrefix = inboxPrefix;
        result.id = id;
        result.request = request;

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
        try
        {
            return core.GetResult(token);
        }
        finally
        {
            core.Reset();
            request = default;
            response = default;
            inboxPrefix = null;
            id = 0;

            if (count < NatsConnection.MaxCommandCacheSize)
            {
                pool.Enqueue(this);
                Interlocked.Increment(ref count);
            }
        }
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

    public void SetResult(TResponse result)
    {
        response = result;
        ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
    }
}
