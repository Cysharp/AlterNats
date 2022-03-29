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
    NatsKey key;
    TRequest? request;
    TResponse? response;
    ReadOnlyMemory<byte> inboxPrefix;
    int id;
    INatsSerializer? serializer;

    RequestAsyncCommand()
    {
    }

    public static RequestAsyncCommand<TRequest, TResponse> Create(in NatsKey key, ReadOnlyMemory<byte> inboxPrefix, int id, TRequest request, INatsSerializer serializer)
    {
        if (!pool.TryDequeue(out var result))
        {
            Interlocked.Decrement(ref count);
            result = new RequestAsyncCommand<TRequest, TResponse>();
        }

        result.key = key;
        result.inboxPrefix = inboxPrefix;
        result.id = id;
        result.request = request;
        result.serializer = serializer;

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
            key = default;
            request = default;
            response = default;
            inboxPrefix = null;
            serializer = null;
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
        writer.WritePublish(key, inboxPrefix, id, request, serializer!);
    }

    public void SetResult(TResponse result)
    {
        response = result;
        ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
    }
}
