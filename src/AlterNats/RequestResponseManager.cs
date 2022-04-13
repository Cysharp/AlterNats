using AlterNats.Commands;
using AlterNats.Internal;
using System.Buffers;
using System.Text;

namespace AlterNats;

// TODO:when connection failed, can not receive response so Dispose All.
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
    IDisposable? globalSubscription; // TODO:when connection failed, null and reuse???

    public RequestResponseManager(NatsConnection connection, ObjectPool pool)
    {
        this.connection = connection;
        this.pool = pool;
    }

    public ValueTask<TResponse?> AddAsync<TRequest, TResponse>(NatsKey key, ReadOnlyMemory<byte> inBoxPrefix, TRequest request)
    {
        if (globalSubscription == null)
        {
            return AddWithGlobalSubscribeAsync<TRequest, TResponse>(key, inBoxPrefix, request);
        }

        return AddAsyncCore<TRequest, TResponse>(key, inBoxPrefix, request);
    }

    async ValueTask<TResponse?> AddWithGlobalSubscribeAsync<TRequest, TResponse>(NatsKey key, ReadOnlyMemory<byte> inBoxPrefix, TRequest request)
    {
        await asyncLock.WaitAsync(cancellationTokenSource.Token);
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

        return await AddAsyncCore<TRequest, TResponse>(key, inBoxPrefix, request).ConfigureAwait(false);
    }

    ValueTask<TResponse?> AddAsyncCore<TRequest, TResponse>(NatsKey key, ReadOnlyMemory<byte> inBoxPrefix, TRequest request)
    {
        var id = Interlocked.Increment(ref requestId);
        var command = RequestAsyncCommand<TRequest, TResponse?>.Create(pool, key, inBoxPrefix, id, request, connection.Options.Serializer);

        lock (gate)
        {
            if (isDisposed) throw new NatsException("Connection is closed.");
            responseBoxes.Add(id, (typeof(TResponse), command));
        }

        connection.PostCommand(command);
        return command.AsValueTask();
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

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        cancellationTokenSource.Cancel();

        lock (gate)
        {
            foreach (var item in responseBoxes)
            {
                if (item.Value.handler is IPromise p)
                {
                    p.SetCanceled(CancellationToken.None);
                }
            }
            responseBoxes.Clear();
        }
    }

}
