using AlterNats.Commands;
using AlterNats.Internal;
using System.Buffers;
using System.Text;

namespace AlterNats;

internal sealed class RequestResponseManager : IDisposable
{
    internal readonly NatsConnection connection;
    readonly ObjectPool pool;
    readonly object gate = new object();
    readonly SemaphoreSlim asyncLock = new SemaphoreSlim(1, 1);
    readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

    int requestId = 0; // unique id per connection
    bool isDisposed;
    // ID: Handler
    Dictionary<int, (Type responseType, object handler)> responseBoxes = new();
    IDisposable? globalSubscription;

    public RequestResponseManager(NatsConnection connection, ObjectPool pool)
    {
        this.connection = connection;
        this.pool = pool;
    }

    public ValueTask<TResponse?> AddAsync<TRequest, TResponse>(NatsKey key, ReadOnlyMemory<byte> inBoxPrefix, TRequest request, CancellationToken cancellationToken)
    {
        if (globalSubscription == null)
        {
            return AddWithGlobalSubscribeAsync<TRequest, TResponse>(key, inBoxPrefix, request, cancellationToken);
        }

        return AddAsyncCoreAsync<TRequest, TResponse>(key, inBoxPrefix, request, cancellationToken);
    }

    async ValueTask<TResponse?> AddWithGlobalSubscribeAsync<TRequest, TResponse>(NatsKey key, ReadOnlyMemory<byte> inBoxPrefix, TRequest request, CancellationToken cancellationToken)
    {
        await asyncLock.WaitAsync(cancellationTokenSource.Token).ConfigureAwait(false);
        try
        {
            if (globalSubscription == null)
            {
                var globalSubscribeKey = $"{Encoding.ASCII.GetString(inBoxPrefix.Span)}*";
                globalSubscription = await connection.SubscribeAsync<byte[]>(globalSubscribeKey, _ => { }).ConfigureAwait(false);
            }
        }
        finally
        {
            asyncLock.Release();
        }

        return await AddAsyncCoreAsync<TRequest, TResponse>(key, inBoxPrefix, request, cancellationToken).ConfigureAwait(false);
    }

    async ValueTask<TResponse?> AddAsyncCoreAsync<TRequest, TResponse>(NatsKey key, ReadOnlyMemory<byte> inBoxPrefix, TRequest request, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref requestId);
        var command = RequestAsyncCommand<TRequest, TResponse?>.Create(pool, key, inBoxPrefix, id, request, connection.Options.Serializer, cancellationToken, this);

        lock (gate)
        {
            if (isDisposed) throw new NatsException("Connection is closed.");
            if (globalSubscription == null) throw new NatsException("Connection is disconnected.");
            responseBoxes.Add(id, (typeof(TResponse), command));
        }

        // MEMO: await has some performance loss, we should avoid await EnqueueAndAwait
        return await connection.EnqueueAndAwaitCommandAsync(command).ConfigureAwait(false);
    }

    public void PublishToResponseHandler(int id, in ReadOnlySequence<byte> buffer)
    {
        (Type responseType, object handler) box;
        lock (gate)
        {
            if (!responseBoxes.Remove(id, out box))
            {
                return;
            }
        }

        ResponsePublisher.PublishResponse(box.responseType, connection.Options, buffer, box.handler);
    }

    public bool Remove(int id)
    {
        lock (gate)
        {
            return responseBoxes.Remove(id, out _);
        }
    }

    // when socket disconnected, can not receive new one so set cancel all waiting promise.
    public void Reset()
    {
        lock (gate)
        {
            foreach (var item in responseBoxes)
            {
                if (item.Value.handler is IPromise p)
                {
                    p.SetCanceled();
                }
            }
            responseBoxes.Clear();

            globalSubscription?.Dispose();
            globalSubscription = null;
        }
    }

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        cancellationTokenSource.Cancel();

        Reset();
    }
}
